using BlazeHarness.Core.Providers;
using BlazeHarness.Core.Sessions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;

namespace BlazeHarness.Core.Agent;

/// <summary>
/// A conversation with a model: the messages the UI shows, the history sent to the model, and the running reply.
/// </summary>
/// <remarks>
/// <para>
/// The provider, model and level are chosen per message, so they can change in the middle of a session.
/// The history holds plain messages only, so any model can continue it, and thinking text is never sent back.
/// </para>
/// <para>
/// A session does not belong to a UI. A reply runs on the thread pool, so it carries on when the page that
/// started it is closed, and any number of pages can read <see cref="Entries"/> and listen to <see cref="Changed"/>.
/// <see cref="Changed"/> is raised on whatever thread made the change, so a UI must marshal to its own context.
/// Only one reply runs at a time.
/// </para>
/// <para>
/// With a <see cref="SessionLog"/>, every message is saved as it arrives, in batches rather than per token.
/// </para>
/// </remarks>
public sealed partial class ChatSession : IDisposable
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(250);

    private readonly Lock _gate = new();
    private readonly Func<ChatTarget, CancellationToken, Task<IChatClient>> _clientFactory;
    private readonly ISessionLog? _log;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly List<ChatEntry> _entries = [];
    private readonly List<ChatMessage> _history = [];
    private IReadOnlyList<ChatEntry> _snapshot = [];
    private Action? _stop;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatSession"/> class.
    /// </summary>
    /// <param name="clientFactory">Creates the chat client for a target. The session disposes the client after each reply.</param>
    /// <param name="log">Where messages are saved, or null to keep the conversation in memory only.</param>
    /// <param name="time">The clock for timestamps and save batching; defaults to the system clock.</param>
    /// <param name="logger">The logger for save failures.</param>
    public ChatSession(
        Func<ChatTarget, CancellationToken, Task<IChatClient>> clientFactory,
        ISessionLog? log = null,
        TimeProvider? time = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        _clientFactory = clientFactory;
        _log = log;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Raised whenever an entry or the running state changes, on the thread that made the change.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the messages in order, as a snapshot that stays valid while the conversation grows.
    /// </summary>
    public IReadOnlyList<ChatEntry> Entries => Volatile.Read(ref _snapshot);

    /// <summary>
    /// Gets a copy of the messages that are sent to the model.
    /// </summary>
    public IReadOnlyList<ChatMessage> History
    {
        get
        {
            lock (_gate)
            {
                return [.. _history];
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether a reply is being produced.
    /// </summary>
    public bool IsRunning => Volatile.Read(ref _stop) is not null;

    /// <summary>
    /// Gets a value indicating whether the last reply failed, was stopped or was interrupted, so it can be retried.
    /// </summary>
    public bool CanRetry => !IsRunning && Entries is [.., { Role: var role, Status: EntryStatus.Failed or EntryStatus.Cancelled or EntryStatus.Interrupted }] && role == ChatRole.Assistant;

    /// <summary>
    /// Rebuild the conversation from a saved log.
    /// </summary>
    /// <remarks>
    /// A reply with no end event was cut off, so it is shown as <see cref="EntryStatus.Interrupted"/> with the text that was saved.
    /// Rewound messages are dropped, as they were when the log was written.
    /// </remarks>
    /// <param name="events">The events read from the log, in order.</param>
    /// <exception cref="InvalidOperationException">The session already has messages or is running.</exception>
    public void Restore(IReadOnlyList<SessionEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        lock (_gate)
        {
            if (_entries.Count > 0 || _stop is not null)
            {
                throw new InvalidOperationException("Only an empty session can be restored.");
            }

            foreach (var e in events)
            {
                switch (e)
                {
                    case UserMessageEvent user:
                        _entries.Add(new ChatEntry(ChatRole.User, user.Text, user.EntryId) { Status = EntryStatus.Done });
                        break;
                    case AssistantStartEvent start:
                        _entries.Add(new ChatEntry(ChatRole.Assistant, id: start.EntryId) { Status = EntryStatus.Interrupted, Label = start.Label });
                        break;
                    case AssistantDeltaEvent delta when Find(delta.EntryId) is { } target:
                        target.AppendText(delta.Text);
                        target.AppendReasoning(delta.Reasoning);
                        break;
                    case AssistantEndEvent end when Find(end.EntryId) is { } target:
                        target.Status = end.Status;
                        target.Error = end.Error;
                        target.Usage = end.Usage;
                        break;
                    case RewindEvent rewind:
                        var keep = _entries.FindIndex(x => x.Id == rewind.ToEntryId);
                        if (keep >= 0)
                        {
                            _entries.RemoveRange(keep + 1, _entries.Count - keep - 1);
                        }

                        break;
                    default:
                        break;
                }
            }

            foreach (var entry in _entries)
            {
                if (entry.Role == ChatRole.Assistant)
                {
                    entry.HistoryIndex = _history.Count;
                }

                if (entry.Text.Length > 0 || entry.Role == ChatRole.User)
                {
                    _history.Add(new ChatMessage(entry.Role, entry.Text));
                }
            }

            Publish();
        }

        RaiseChanged();
    }

    /// <summary>
    /// Send a user message and stream the reply.
    /// </summary>
    /// <param name="text">The message text.</param>
    /// <param name="target">The provider, model and level to use for this message.</param>
    /// <returns>A task that completes when the reply has ended; it does nothing when a reply is running or the text is empty.</returns>
    public Task SendAsync(string text, ChatTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.CompletedTask;
        }

        var user = new ChatEntry(ChatRole.User, text) { Status = EntryStatus.Done };
        return TurnAsync(
            target,
            () =>
            {
                _entries.Add(user);
                _history.Add(new ChatMessage(ChatRole.User, text));
                return true;
            },
            () => [new UserMessageEvent(_time.GetUtcNow(), user.Id, text)]);
    }

    /// <summary>
    /// Request a new reply to the last user message after the last reply failed, was stopped or was interrupted.
    /// </summary>
    /// <remarks>
    /// The failed reply, including any partial text, is dropped from the history, so the model never sees it.
    /// </remarks>
    /// <param name="target">The provider, model and level to use, which may differ from the failed attempt.</param>
    /// <returns>A task that completes when the reply has ended; it does nothing when the last reply can't be retried.</returns>
    public Task RetryAsync(ChatTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var keep = Guid.Empty;
        return TurnAsync(
            target,
            () =>
            {
                if (_entries is not [.., { Id: var kept }, { Role: ChatRole role, Status: EntryStatus.Failed or EntryStatus.Cancelled or EntryStatus.Interrupted } failed] || role != ChatRole.Assistant)
                {
                    return false;
                }

                keep = kept;
                _entries.RemoveAt(_entries.Count - 1);
                _history.RemoveRange(failed.HistoryIndex, _history.Count - failed.HistoryIndex);
                return true;
            },
            () => [new RewindEvent(_time.GetUtcNow(), keep)]);
    }

    /// <summary>
    /// Stop any reply that is being produced, so nothing keeps running after the session is gone.
    /// </summary>
    public void Dispose()
    {
        Stop();
    }

    /// <summary>
    /// Stop the reply that is being produced, keeping the text received so far.
    /// </summary>
    public void Stop()
    {
        try
        {
            Volatile.Read(ref _stop)?.Invoke();
        }
        catch (ObjectDisposedException)
        {
            // The reply ended just before the stop arrived.
        }
    }

    private async Task TurnAsync(ChatTarget target, Func<bool> change, Func<IReadOnlyList<SessionEvent>> leading)
    {
        using var run = new CancellationTokenSource();
        lock (_gate)
        {
            if (_stop is not null || !change())
            {
                return;
            }

            _stop = run.Cancel;
            Publish();
        }

        RaiseChanged();
        try
        {
            await Task.Run(() => ReplyAsync(target, leading(), run.Token), CancellationToken.None);
        }
        finally
        {
            Volatile.Write(ref _stop, null);
            RaiseChanged();
        }
    }

    private ChatEntry? Find(Guid id) => _entries.Find(e => e.Id == id);

    private void Publish() => Volatile.Write(ref _snapshot, _entries.ToArray());

    private async Task ReplyAsync(ChatTarget target, IReadOnlyList<SessionEvent> leading, CancellationToken cancellation)
    {
        var entry = new ChatEntry(ChatRole.Assistant)
        {
            Status = EntryStatus.Streaming,
            Label = target.Label,
        };
        lock (_gate)
        {
            entry.HistoryIndex = _history.Count;
            _entries.Add(entry);
            Publish();
        }

        await SaveAsync([.. leading, new AssistantStartEvent(_time.GetUtcNow(), entry.Id, entry.Label)]);
        RaiseChanged();

        var pendingText = new StringBuilder();
        var pendingReasoning = new StringBuilder();
        var lastSave = _time.GetTimestamp();
        List<ChatMessage> working = [.. History];
        var before = working.Count;

        try
        {
            using var client = await _clientFactory(target, cancellation);
            var options = ChatClientFactory.BuildOptions(target.Provider, target.Model, target.Level, target.SessionOverride);
            await foreach (var e in ChatRunner.RunAsync(client, working, options, cancellation))
            {
                Apply(entry, e, pendingText, pendingReasoning);
                if ((pendingText.Length > 0 || pendingReasoning.Length > 0) && _time.GetElapsedTime(lastSave) >= FlushInterval)
                {
                    await SaveAsync([TakeDelta(entry, pendingText, pendingReasoning)]);
                    lastSave = _time.GetTimestamp();
                }

                RaiseChanged();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            entry.Status = EntryStatus.Cancelled;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            entry.Status = EntryStatus.Failed;
            entry.Error = ex.Message;
        }
        finally
        {
            if (entry.Status == EntryStatus.Streaming)
            {
                entry.Status = EntryStatus.Done;
            }

            lock (_gate)
            {
                _history.AddRange(working.Skip(before));
            }

            List<SessionEvent> closing = [];
            if (pendingText.Length > 0 || pendingReasoning.Length > 0)
            {
                closing.Add(TakeDelta(entry, pendingText, pendingReasoning));
            }

            closing.Add(new AssistantEndEvent(_time.GetUtcNow(), entry.Id, entry.Status, entry.Error, entry.Usage));
            await SaveAsync(closing);
        }
    }

    private static void Apply(ChatEntry entry, AgentEvent e, StringBuilder pendingText, StringBuilder pendingReasoning)
    {
        switch (e)
        {
            case TextDelta text:
                entry.AppendText(text.Text);
                pendingText.Append(text.Text);
                break;
            case ReasoningDelta thinking:
                entry.AppendReasoning(thinking.Text);
                pendingReasoning.Append(thinking.Text);
                break;
            case UsageReported usage:
                entry.Usage = usage;
                break;
            case AgentError error:
                entry.Error = error.Message;
                break;
            case Done done:
                entry.Status = done.Reason switch
                {
                    StopReason.Cancelled => EntryStatus.Cancelled,
                    StopReason.Error => EntryStatus.Failed,
                    _ => EntryStatus.Done,
                };
                break;
            default:
                break;
        }
    }

    private AssistantDeltaEvent TakeDelta(ChatEntry entry, StringBuilder text, StringBuilder reasoning)
    {
        var delta = new AssistantDeltaEvent(_time.GetUtcNow(), entry.Id, text.ToString(), reasoning.ToString());
        text.Clear();
        reasoning.Clear();
        return delta;
    }

    private async Task SaveAsync(IReadOnlyCollection<SessionEvent> events)
    {
        if (_log is null)
        {
            return;
        }

        try
        {
            await _log.AppendAsync(events);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSaveFailed(ex);
        }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not save messages to the session log; the conversation continues in memory.")]
    private partial void LogSaveFailed(Exception ex);
}
