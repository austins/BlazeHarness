using BlazeHarness.Core.Agent.Questions;
using BlazeHarness.Core.Attachments;
using BlazeHarness.Core.Chats;
using BlazeHarness.Core.Chats.Events;
using BlazeHarness.Core.Compaction;
using BlazeHarness.Core.Folders;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Skills;
using BlazeHarness.Core.Tools;
using BlazeHarness.Core.Tools.Shell;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Text.Json;

namespace BlazeHarness.Core.Agent;

/// <summary>
/// A conversation with a model: the messages the UI shows, the history sent to the model, and the running reply.
/// </summary>
/// <remarks>
/// <para>
/// The provider, model and level are chosen per message, so they can change in the middle of a chat.
/// The history holds plain messages only, so any model can continue it, and thinking text is never sent back.
/// </para>
/// <para>
/// A chat does not belong to a UI. A reply runs on the thread pool, so it carries on when the page that
/// started it is closed, and any number of pages can read <see cref="Entries"/> and listen to <see cref="Changed"/>.
/// <see cref="Changed"/> is raised on whatever thread made the change, so a UI must marshal to its own context.
/// Only one reply runs at a time.
/// </para>
/// <para>
/// With a <see cref="ChatLog"/>, every message is saved as it arrives, in batches rather than per token.
/// </para>
/// </remarks>
public sealed partial class Chat : IDisposable, IToolApprover, IRunInbox, IUserAsker
{
    /// <summary>
    /// How long a tool call waits for an answer before it is denied, so a turn nobody is watching ends by itself.
    /// </summary>
    public static readonly TimeSpan ApprovalTimeout = TimeSpan.FromMinutes(30);

    // Stands in for a reply that ended with nothing, so the next user message doesn't follow another one, which strict chat templates reject.
    internal const string EmptyReplyText = "(No reply was produced.)";

    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(250);

    private readonly Lock _gate = new();
    private readonly Func<ChatTarget, CancellationToken, Task<IChatClient>> _clientFactory;
    private readonly IChatLog? _log;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly List<ChatEntry> _entries = [];
    private readonly List<ChatMessage> _history = [];
    private readonly List<string> _queued = [];
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ApprovalChoice>> _approvals = new(StringComparer.Ordinal);
    private readonly TodoCache _todoCache = new();
    private IReadOnlyList<ChatEntry> _snapshot = [];
    private ProjectContext? _project;
    private SkillSet? _skills;
    private ToolSetup _setup = new(null, null);
    private Func<CancellationToken, Task<IReadOnlyList<ITool>>>? _extraTools;
    private ChatAttachments? _attachments;
    private Action? _stop;
    private bool _replying;
    private bool _closed;

    /// <summary>
    /// Initializes a new instance of the <see cref="Chat"/> class.
    /// </summary>
    /// <param name="clientFactory">Creates the chat client for a target. The chat disposes the client after each reply.</param>
    /// <param name="time">The clock for timestamps and save batching.</param>
    /// <param name="log">Where messages are saved, or null to keep the conversation in memory only.</param>
    /// <param name="logger">The logger for save failures.</param>
    public Chat(
        Func<ChatTarget, CancellationToken, Task<IChatClient>> clientFactory,
        TimeProvider time,
        IChatLog? log = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(time);
        _clientFactory = clientFactory;
        _log = log;
        _time = time;
        _logger = logger ?? NullLogger.Instance;
        BackgroundShells = new BackgroundShells(time);
        BackgroundShells.Changed += (_, _) => RaiseChanged();
    }

    /// <summary>
    /// Raised whenever an entry or the running state changes, on the thread that made the change.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the commands the agent started in the background, which end when the chat is disposed.
    /// </summary>
    public BackgroundShells BackgroundShells { get; }

    /// <summary>
    /// Gets the messages in order, as a snapshot that stays valid while the conversation grows.
    /// </summary>
    public IReadOnlyList<ChatEntry> Entries => Volatile.Read(ref _snapshot);

    /// <summary>
    /// Gets the agent's to-do list, which is the one the last successful <c>update_todos</c> call set.
    /// </summary>
    /// <remarks>
    /// It is read from the messages each time, so it follows a rewind, an edit and a restored chat without being saved on its own.
    /// </remarks>
    public TodoList Todos => TodoList.FromCached(Entries, _todoCache);

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
    /// Gets or sets the project instructions sent to the model as a system message, or null for none.
    /// </summary>
    /// <remarks>
    /// A reply reads this once when it starts, so changing it never alters a reply that is running.
    /// </remarks>
    public ProjectContext? Project
    {
        get => Volatile.Read(ref _project);
        set
        {
            Volatile.Write(ref _project, value);
            RaiseChanged();
        }
    }

    /// <summary>
    /// Gets or sets the skills the chat offers the model, or null for none.
    /// </summary>
    /// <remarks>
    /// They are listed in the system prompt and loaded with the <c>load_skill</c> tool, so they only apply while the chat has tools.
    /// A reply reads this once when it starts, so changing it never alters a reply that is running.
    /// </remarks>
    public SkillSet? Skills
    {
        get => Volatile.Read(ref _skills);
        set
        {
            Volatile.Write(ref _skills, value);
            RaiseChanged();
        }
    }

    /// <summary>
    /// Gets or sets where the chat's attached files are kept, or null when it can't take attachments.
    /// </summary>
    /// <remarks>
    /// Set it before <see cref="Restore"/>, because rebuilding the history reads the attached files.
    /// </remarks>
    public ChatAttachments? Attachments
    {
        get => Volatile.Read(ref _attachments);
        set => Volatile.Write(ref _attachments, value);
    }

    /// <summary>
    /// Gets or sets the base system prompt that comes before the project instructions, or null for none.
    /// </summary>
    /// <remarks>
    /// A reply reads this once when it starts, so changing it never alters a reply that is running.
    /// Use <see cref="UseTools"/> to change it together with <see cref="Tools"/>.
    /// </remarks>
    public string? BasePrompt
    {
        get => Volatile.Read(ref _setup).BasePrompt;
        set => UpdateSetup(current => current with { BasePrompt = value });
    }

    /// <summary>
    /// Gets the permission check for this chat's tool calls, with the chat's settings and approvals it remembers.
    /// </summary>
    public PermissionEngine Permissions { get; } = new();

    /// <summary>
    /// Gets or sets the tools the model may call, or null for none.
    /// </summary>
    /// <remarks>
    /// A reply reads this once when it starts, so changing it never alters a reply that is running.
    /// Use <see cref="UseTools"/> to change it together with <see cref="BasePrompt"/>.
    /// </remarks>
    public ToolSet? Tools
    {
        get => Volatile.Read(ref _setup).Tools;
        set => UpdateSetup(current => current with { Tools = value });
    }

    /// <summary>
    /// Set the tools and the base system prompt in one step, so a reply never sees the tools of one with the prompt of the other.
    /// </summary>
    /// <param name="tools">The tools the model may call, or null for none.</param>
    /// <param name="basePrompt">The base system prompt, or null for none.</param>
    public void UseTools(ToolSet? tools, string? basePrompt) => Volatile.Write(ref _setup, new ToolSetup(tools, basePrompt));

    /// <summary>
    /// Gets or sets where the tools that come and go between replies, such as MCP tools, are asked for.
    /// </summary>
    /// <remarks>
    /// A reply asks once when it starts and keeps those tools until it ends, so the list the model sees never changes within a reply.
    /// The tools are added to <see cref="Tools"/>, and are not offered when that is null.
    /// </remarks>
    public Func<CancellationToken, Task<IReadOnlyList<ITool>>>? ExtraTools
    {
        get => Volatile.Read(ref _extraTools);
        set => Volatile.Write(ref _extraTools, value);
    }

    /// <summary>
    /// Gets or sets where the way summaries are written is asked for each time a conversation is compacted, or null to always use the model.
    /// </summary>
    public Func<CompactionMode>? CompactionModeSource { get; set; }

    /// <summary>
    /// Gets a value indicating whether a reply is being produced.
    /// </summary>
    public bool IsRunning => Volatile.Read(ref _stop) is not null;

    /// <summary>
    /// Gets a value indicating whether a reply is being produced, which is when <see cref="Queue"/> accepts a message.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="IsRunning"/>, it is false while the chat is only being compacted.
    /// </remarks>
    public bool IsReplying
    {
        get
        {
            lock (_gate)
            {
                return _replying;
            }
        }
    }

    /// <summary>
    /// Gets the messages that wait to be added to the running reply, oldest first.
    /// </summary>
    /// <remarks>
    /// After a reply that was stopped or failed, what was still waiting stays here until <see cref="TakeQueued"/> takes it.
    /// </remarks>
    public IReadOnlyList<string> Queued
    {
        get
        {
            lock (_gate)
            {
                return [.. _queued];
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the last reply failed, was stopped or was interrupted, or ended right after a message that was added to it, so it can be retried.
    /// </summary>
    public bool CanRetry => !IsRunning && Entries is [.., { Role: var role, Status: var status, Origin: var origin }]
        && (role == ChatRole.Assistant ? status is EntryStatus.Failed or EntryStatus.Cancelled or EntryStatus.Interrupted : role == ChatRole.User && origin is not null);

    /// <summary>
    /// Rebuild the conversation from a saved log.
    /// </summary>
    /// <remarks>
    /// A reply with no end event was cut off, so it is shown as <see cref="EntryStatus.Interrupted"/> with the text that was saved,
    /// and so is a reply whose tool calls had not all finished. Those calls are shown as <see cref="ToolCallStatus.Interrupted"/>.
    /// Rewound messages are dropped, as they were when the log was written.
    /// </remarks>
    /// <param name="events">The events read from the log, in order.</param>
    /// <exception cref="InvalidOperationException">The chat already has messages or is running.</exception>
    public void Restore(IReadOnlyList<ChatEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        lock (_gate)
        {
            EnsureRestorable();
        }

        // Rebuilding the history reads the attached files, so it happens before the lock is taken.
        var entries = ChatReplay.Replay(events);
        List<ChatMessage> history = [];
        foreach (var entry in entries)
        {
            entry.HistoryIndex = history.Count;
            AppendHistory(history, entry, Attachments);
        }

        lock (_gate)
        {
            EnsureRestorable();
            _entries.AddRange(entries);
            _history.AddRange(history);
            RefreshCompaction();
            _compactedAfter = _compaction is null ? null : _entries[^1];
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
    public Task SendAsync(string text, ChatTarget target) => SendAsync(text, [], target);

    /// <summary>
    /// Send a user message with attached files and stream the reply.
    /// </summary>
    /// <remarks>
    /// How each file reaches the model is decided here from the target model, and is kept with the message.
    /// </remarks>
    /// <param name="text">The message text, which may be empty when files are attached.</param>
    /// <param name="files">The files, already saved with <see cref="Attachments"/>.</param>
    /// <param name="target">The provider, model and level to use for this message.</param>
    /// <returns>A task that completes when the reply has ended; it does nothing when a reply is running or there is neither text nor a file.</returns>
    /// <exception cref="ArgumentException">
    /// There are more than <see cref="ChatAttachments.MaxFilesPerMessage"/> files, they add up to more than <see cref="ChatAttachments.MaxMessageBytes"/>,
    /// or a file was not saved with this chat.
    /// </exception>
    public Task SendAsync(string text, IReadOnlyList<AttachmentInfo> files, ChatTarget target)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(target);
        if (files.Count > ChatAttachments.MaxFilesPerMessage)
        {
            throw new ArgumentException($"A message can have at most {ChatAttachments.MaxFilesPerMessage} files.", nameof(files));
        }

        if (files.Sum(f => f.Size) > ChatAttachments.MaxMessageBytes)
        {
            throw new ArgumentException($"A message can have at most {ChatAttachments.MaxMessageBytes / (1024 * 1024)} MB of files.", nameof(files));
        }

        if (files.Any(f => Attachments?.Find(f.Id) is null))
        {
            throw new ArgumentException("A file was not saved with this chat.", nameof(files));
        }

        if ((string.IsNullOrWhiteSpace(text) && files.Count == 0) || IsRunning)
        {
            return Task.CompletedTask;
        }

        var sentAt = _time.GetUtcNow();
        var sent = AttachmentPolicy.Plan(files, target.Model);
        var user = new ChatEntry(ChatRole.User, text) { Status = EntryStatus.Done, At = sentAt, Attachments = sent };

        // Reading the files can be slow, so it happens before the lock that readers of the history wait on.
        var message = AttachmentMessage.Build(text, sent, Attachments);
        return TurnAsync(
            target,
            () =>
            {
                user.HistoryIndex = _history.Count;
                _entries.Add(user);
                _history.Add(message);
                return true;
            },
            () => [new UserMessageEvent(sentAt, user.Id, text, sent)]);
    }

    /// <summary>
    /// Queue a message for the reply that is running.
    /// </summary>
    /// <remarks>
    /// The message is added to the conversation after the tool results of the current round, or when the model would otherwise finish, and the reply goes on.
    /// Messages that wait together are merged into one. A message still waiting when the reply is stopped or fails is kept in <see cref="Queued"/>.
    /// While an <c>ask_user</c> question waits, the message is its free-text answer instead and nothing is queued.
    /// </remarks>
    /// <param name="text">The message text. Attachments are not supported.</param>
    /// <returns>True when the message was queued or answered a question; false when no reply is running, the chat is closed, or the text is blank.</returns>
    public bool Queue(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (TryAnswerWithText(text))
        {
            return true;
        }

        lock (_gate)
        {
            if (!_replying || _closed)
            {
                return false;
            }

            _queued.Add(text);
        }

        RaiseChanged();
        return true;
    }

    /// <summary>
    /// Take the messages that wait, for example to give them back to the user after a stop.
    /// </summary>
    /// <returns>The messages, oldest first; empty when none waited.</returns>
    public IReadOnlyList<string> TakeQueued()
    {
        string[] taken;
        lock (_gate)
        {
            taken = [.. _queued];
            _queued.Clear();
        }

        if (taken.Length > 0)
        {
            RaiseChanged();
        }

        return taken;
    }

    /// <summary>
    /// Request a new reply after the last reply failed, was stopped or was interrupted.
    /// </summary>
    /// <remarks>
    /// A reply with no tool calls is dropped from the history, including any partial text, so the model never sees it.
    /// A reply that made tool calls stays, since the calls may have changed things, and the model continues from their results.
    /// </remarks>
    /// <param name="target">The provider, model and level to use, which may differ from the failed attempt.</param>
    /// <returns>A task that completes when the reply has ended; it does nothing when the last reply can't be retried.</returns>
    public Task RetryAsync(ChatTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        List<ChatEvent> rewind = [];
        return TurnAsync(
            target,
            () =>
            {
                // A message added to a reply that then ended has no reply of its own yet, so the retry just answers it.
                if (_entries is [.., { Role: var last, Origin: not null }] && last == ChatRole.User)
                {
                    return true;
                }

                if (_entries is not [.., { Role: var role, Status: EntryStatus.Failed or EntryStatus.Cancelled or EntryStatus.Interrupted } failed] || role != ChatRole.Assistant)
                {
                    return false;
                }

                if (failed.ToolCalls.Count == 0)
                {
                    _entries.RemoveAt(_entries.Count - 1);
                    _history.RemoveRange(failed.HistoryIndex, _history.Count - failed.HistoryIndex);
                    rewind.Add(new RewindEvent(_time.GetUtcNow(), _entries is [.., { Id: var kept }] ? kept : Guid.Empty));
                    RefreshCompaction();
                }

                return true;
            },
            () => rewind);
    }

    /// <summary>
    /// Get whether a message can be edited and sent again.
    /// </summary>
    /// <param name="entryId">The id of the message.</param>
    /// <returns>True when the message is one of the user's and no reply is running.</returns>
    public bool CanEdit(Guid entryId)
    {
        lock (_gate)
        {
            return _stop is null && _entries.Find(e => e.Id == entryId) is { Role: var role } && role == ChatRole.User;
        }
    }

    /// <summary>
    /// Replace one of the user's messages and stream a new reply to it.
    /// </summary>
    /// <remarks>
    /// The old message and everything after it is dropped from the conversation, so the model never sees it.
    /// The log keeps it behind a rewind marker.
    /// </remarks>
    /// <param name="entryId">The id of the message to replace.</param>
    /// <param name="text">The new message text.</param>
    /// <param name="target">The provider, model and level to use for the new reply.</param>
    /// <returns>A task that completes when the reply has ended; it does nothing when the message can't be edited, a reply is running or the text is empty.</returns>
    public Task EditAsync(Guid entryId, string text, ChatTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.CompletedTask;
        }

        IReadOnlyList<SentAttachment>? previous;
        lock (_gate)
        {
            previous = _stop is null ? _entries.Find(e => e.Id == entryId && e.Role == ChatRole.User)?.Attachments : null;
        }

        if (previous is null)
        {
            return Task.CompletedTask;
        }

        // The files were planned for the model the message first went to; this reply may go to another one.
        var sentAt = _time.GetUtcNow();
        var sent = AttachmentPolicy.Plan([.. previous.Select(a => a.File)], target.Model);
        var user = new ChatEntry(ChatRole.User, text) { Status = EntryStatus.Done, At = sentAt, Attachments = sent };
        var message = AttachmentMessage.Build(text, sent, Attachments);
        var keep = Guid.Empty;
        return TurnAsync(
            target,
            () =>
            {
                var index = _entries.FindIndex(e => e.Id == entryId && e.Role == ChatRole.User);
                if (index < 0)
                {
                    return false;
                }

                keep = index == 0 ? Guid.Empty : _entries[index - 1].Id;
                var historyIndex = _entries[index].HistoryIndex;
                _entries.RemoveRange(index, _entries.Count - index);
                _history.RemoveRange(historyIndex, _history.Count - historyIndex);
                user.HistoryIndex = _history.Count;
                _entries.Add(user);
                _history.Add(message);
                RefreshCompaction();
                return true;
            },
            () => [new RewindEvent(sentAt, keep), new UserMessageEvent(sentAt, user.Id, text, sent)]);
    }

    /// <summary>
    /// Answer a tool call that waits for approval.
    /// </summary>
    /// <param name="callId">The id of the call, from <see cref="ToolCallEntry.CallId"/>.</param>
    /// <param name="choice">Whether the call may run, and whether that holds for the rest of the chat.</param>
    /// <returns>True when a call was waiting for the answer; false when it had already been answered or stopped.</returns>
    public bool Approve(string callId, ApprovalChoice choice)
    {
        ArgumentNullException.ThrowIfNull(callId);
        return _approvals.TryGetValue(callId, out var waiting) && waiting.TrySetResult(choice);
    }

    /// <summary>
    /// Change the access level and approvals the chat runs with, and record the change in its log.
    /// </summary>
    /// <remarks>
    /// A call that is already running keeps what it was allowed; the next call is checked against the new settings.
    /// </remarks>
    /// <param name="settings">The new settings.</param>
    /// <returns>A task that completes once the change is saved.</returns>
    public async Task SetPermissionsAsync(PermissionSettings settings)
    {
        if (Permissions.Settings == settings)
        {
            return;
        }

        Permissions.Settings = settings;
        await SaveAsync([new PermissionsChangedEvent(_time.GetUtcNow(), settings.Access, settings.Approvals)]);
        RaiseChanged();
    }

    /// <inheritdoc />
    string? IRunInbox.TakeMessages()
    {
        lock (_gate)
        {
            // A waiting question is answered by the user's next message; a steer message must not jump the queue.
            if (_queued.Count == 0 || IsWaitingForUser)
            {
                return null;
            }

            var text = string.Join("\n\n", _queued);
            _queued.Clear();
            return text;
        }
    }

    /// <inheritdoc />
    Task<ApprovalChoice> IToolApprover.RequestAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        return WaitForApprovalAsync(request.CallId, cancellationToken);
    }

    /// <summary>
    /// Stop any reply that is being produced and every background command, so nothing keeps running after the chat is gone.
    /// </summary>
    public void Dispose()
    {
        Stop();
        BackgroundShells.Dispose();
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

    /// <summary>
    /// Close the chat to new replies unless one is running; the check and the close happen together.
    /// </summary>
    /// <returns>False when a reply is running and the chat was left open.</returns>
    internal bool TryClose()
    {
        lock (_gate)
        {
            if (_stop is not null)
            {
                return false;
            }

            _closed = true;
            return true;
        }
    }

    private async Task TurnAsync(ChatTarget target, Func<bool> change, Func<IReadOnlyList<ChatEvent>> leading)
    {
        using var run = new CancellationTokenSource();
        lock (_gate)
        {
            if (_stop is not null || _closed || !change())
            {
                return;
            }

            // What an earlier reply left queued was shown to the user to take back; it must not leak into this one.
            _queued.Clear();
            _stop = run.Cancel;
            _replying = true;
            Publish();
        }

        RaiseChanged();
        try
        {
            var completed = await Task.Run(() => ReplyAsync(target, leading(), run.Token), CancellationToken.None);
            while (TakeFollowUp(completed && !run.IsCancellationRequested) is { } next)
            {
                RaiseChanged();
                completed = await Task.Run(() => ReplyAsync(target, next, run.Token), CancellationToken.None);
            }
        }
        finally
        {
            EndTurn();
            RaiseChanged();
        }
    }

    // A message that was queued too late for the running reply starts the next one, but only after a normal finish.
    // Taking it and ending the turn happen under one lock, so a message is either taken here or refused by Queue.
    private IReadOnlyList<ChatEvent>? TakeFollowUp(bool finishedNormally)
    {
        lock (_gate)
        {
            if (!finishedNormally || _closed || _queued.Count == 0)
            {
                EndTurn();
                return null;
            }

            var text = string.Join("\n\n", _queued);
            _queued.Clear();
            var sentAt = _time.GetUtcNow();
            var user = new ChatEntry(ChatRole.User, text) { Status = EntryStatus.Done, At = sentAt, Origin = UserMessageOrigin.Steer, HistoryIndex = _history.Count };
            _entries.Add(user);
            _history.Add(new ChatMessage(ChatRole.User, text));
            Publish();
            return [new UserMessageEvent(sentAt, user.Id, text, null, UserMessageOrigin.Steer)];
        }
    }

    private void EndTurn()
    {
        lock (_gate)
        {
            _replying = false;
            Volatile.Write(ref _stop, null);
        }
    }

    private static void AppendHistory(List<ChatMessage> history, ChatEntry entry, ChatAttachments? attachments)
    {
        if (entry.Compaction is not null)
        {
            return;
        }

        if (entry.Role != ChatRole.Assistant)
        {
            history.Add(AttachmentMessage.Build(entry.Text, entry.Attachments, attachments));
            return;
        }

        List<AIContent> contents = [];
        if (entry.Text.Length > 0)
        {
            contents.Add(new TextContent(entry.Text));
        }

        contents.AddRange(entry.ToolCalls.Select(c => new FunctionCallContent(c.CallId, c.Name, ParseArguments(c.ArgumentsJson))));
        if (contents.Count == 0)
        {
            history.Add(new ChatMessage(ChatRole.Assistant, EmptyReplyText));
            return;
        }

        if (entry.Reasoning.Length > 0)
        {
            contents.Insert(0, new TextReasoningContent(entry.Reasoning));
        }

        history.Add(new ChatMessage(ChatRole.Assistant, contents));
        if (entry.ToolCalls.Count > 0)
        {
            history.Add(new ChatMessage(ChatRole.Tool, [.. entry.ToolCalls.Select(c => new FunctionResultContent(c.CallId, ToolCallEntry.ForModel(c.Status, c.Output)))]));
            if (AttachmentMessage.ForToolImages(entry.ToolCalls, attachments) is { } pictures)
            {
                history.Add(pictures);
            }
        }
    }

    private static Dictionary<string, object?>? ParseArguments(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json, AIJsonUtilities.DefaultOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<ApprovalChoice> WaitForApprovalAsync(string callId, CancellationToken cancellationToken)
    {
        var waiting = new TaskCompletionSource<ApprovalChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        _approvals[callId] = waiting;
        using var registration = cancellationToken.Register(() => waiting.TrySetCanceled(cancellationToken));
        using var expiry = new CancellationTokenSource(ApprovalTimeout, _time);
        using var expiryRegistration = expiry.Token.Register(() => waiting.TrySetResult(ApprovalChoice.Deny));
        try
        {
            return await waiting.Task;
        }
        finally
        {
            _approvals.TryRemove(callId, out _);
        }
    }

    // Must be called with the gate held.
    private void EnsureRestorable()
    {
        if (_entries.Count > 0 || _stop is not null)
        {
            throw new InvalidOperationException("Only an empty chat can be restored.");
        }
    }

    private void UpdateSetup(Func<ToolSetup, ToolSetup> change)
    {
        ToolSetup current;
        ToolSetup next;
        do
        {
            current = Volatile.Read(ref _setup);
            next = change(current);
        }
        while (!ReferenceEquals(Interlocked.CompareExchange(ref _setup, next, current), current));
    }

    private void Publish() => Volatile.Write(ref _snapshot, _entries.ToArray());

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not save messages to the chat log; the conversation continues in memory.")]
    private partial void LogSaveFailed(Exception ex);
}
