using BlazeHarness.Core.Providers;
using Microsoft.Extensions.AI;

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
/// A session is meant for one synchronization context, such as a Blazor circuit. Its <see cref="Changed"/> event
/// is raised on that context, and the UI throttles its own rendering.
/// </para>
/// </remarks>
public sealed class ChatSession : IDisposable
{
    private readonly Func<ChatTarget, CancellationToken, Task<IChatClient>> _clientFactory;
    private readonly List<ChatEntry> _entries = [];
    private readonly List<ChatMessage> _history = [];
    private Action? _stop;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatSession"/> class.
    /// </summary>
    /// <param name="clientFactory">Creates the chat client for a target. The session disposes the client after each reply.</param>
    public ChatSession(Func<ChatTarget, CancellationToken, Task<IChatClient>> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        _clientFactory = clientFactory;
    }

    /// <summary>
    /// Raised whenever an entry or the running state changes.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the messages in order.
    /// </summary>
    public IReadOnlyList<ChatEntry> Entries => _entries;

    /// <summary>
    /// Gets the messages that are sent to the model.
    /// </summary>
    public IReadOnlyList<ChatMessage> History => _history;

    /// <summary>
    /// Gets a value indicating whether a reply is being produced.
    /// </summary>
    public bool IsRunning => _stop is not null;

    /// <summary>
    /// Gets a value indicating whether the last reply failed or was stopped, so it can be retried.
    /// </summary>
    public bool CanRetry => !IsRunning && _entries is [.., { Role: var role, Status: EntryStatus.Failed or EntryStatus.Cancelled }] && role == ChatRole.Assistant;

    /// <summary>
    /// Send a user message and stream the reply.
    /// </summary>
    /// <param name="text">The message text.</param>
    /// <param name="target">The provider, model and level to use for this message.</param>
    /// <returns>A task that completes when the reply has ended; it does nothing when a reply is running or the text is empty.</returns>
    public async Task SendAsync(string text, ChatTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (IsRunning || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _entries.Add(new ChatEntry(ChatRole.User, text) { Status = EntryStatus.Done });
        _history.Add(new ChatMessage(ChatRole.User, text));
        await ReplyAsync(target);
    }

    /// <summary>
    /// Request a new reply to the last user message after the last reply failed or was stopped.
    /// </summary>
    /// <remarks>
    /// The failed reply, including any partial text, is dropped from the history, so the model never sees it.
    /// </remarks>
    /// <param name="target">The provider, model and level to use, which may differ from the failed attempt.</param>
    /// <returns>A task that completes when the reply has ended; it does nothing when the last reply can't be retried.</returns>
    public async Task RetryAsync(ChatTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!CanRetry)
        {
            return;
        }

        var failed = _entries[^1];
        _entries.RemoveAt(_entries.Count - 1);
        _history.RemoveRange(failed.HistoryIndex, _history.Count - failed.HistoryIndex);
        await ReplyAsync(target);
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
        _stop?.Invoke();
    }

    private async Task ReplyAsync(ChatTarget target)
    {
        var entry = new ChatEntry(ChatRole.Assistant)
        {
            Status = EntryStatus.Streaming,
            Label = target.Label,
            HistoryIndex = _history.Count,
        };
        _entries.Add(entry);

        using var run = new CancellationTokenSource();
        _stop = run.Cancel;
        RaiseChanged();

        try
        {
            using var client = await _clientFactory(target, run.Token);
            var options = ChatClientFactory.BuildOptions(target.Provider, target.Model, target.Level, null);
            await foreach (var e in ChatRunner.RunAsync(client, _history, options, run.Token))
            {
                Apply(entry, e);
                RaiseChanged();
            }
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
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

            _stop = null;
            RaiseChanged();
        }
    }

    private static void Apply(ChatEntry entry, AgentEvent e)
    {
        switch (e)
        {
            case TextDelta text:
                entry.AppendText(text.Text);
                break;
            case ReasoningDelta thinking:
                entry.AppendReasoning(thinking.Text);
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

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
