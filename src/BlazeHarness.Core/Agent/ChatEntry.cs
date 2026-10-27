using BlazeHarness.Core.Attachments;
using BlazeHarness.Core.Compaction;
using Microsoft.Extensions.AI;
using System.Text;

namespace BlazeHarness.Core.Agent;

/// <summary>
/// The state of one message in the conversation, as the UI shows it.
/// </summary>
public enum EntryStatus
{
    /// <summary>The message is complete.</summary>
    Done,

    /// <summary>The reply is still arriving.</summary>
    Streaming,

    /// <summary>The reply failed; see <see cref="ChatEntry.Error"/>.</summary>
    Failed,

    /// <summary>The user stopped the reply, which keeps the text received so far.</summary>
    Cancelled,

    /// <summary>The app stopped while the reply was arriving, so it ends where the saved text does.</summary>
    Interrupted,
}

/// <summary>
/// One message in a conversation, as the UI shows it.
/// </summary>
/// <remarks>
/// The text is safe to read while a reply streams in on another thread. Other properties are replaced as whole values.
/// </remarks>
public sealed class ChatEntry
{
    private readonly Lock _gate = new();
    private readonly StringBuilder _text = new();
    private readonly StringBuilder _reasoning = new();
    private string? _textCache;
    private string? _reasoningCache;
    private long _atTicks;
    private IReadOnlyList<ToolCallEntry> _toolCalls = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatEntry"/> class.
    /// </summary>
    /// <param name="role">Who wrote the message.</param>
    /// <param name="text">The initial text.</param>
    /// <param name="id">The id of the message, or null for a new one; a saved message keeps its id.</param>
    public ChatEntry(ChatRole role, string text = "", Guid? id = null)
    {
        Role = role;
        Id = id ?? Guid.NewGuid();
        _text.Append(text);
    }

    /// <summary>
    /// Gets a unique id, which the UI uses to keep elements matched to messages and the log refers to.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets who wrote the message.
    /// </summary>
    public ChatRole Role { get; }

    /// <summary>
    /// Gets the visible text.
    /// </summary>
    public string Text
    {
        get
        {
            lock (_gate)
            {
                return _textCache ??= _text.ToString();
            }
        }
    }

    /// <summary>
    /// Gets the thinking text the model streamed, which is shown but never sent back to the model.
    /// </summary>
    public string Reasoning
    {
        get
        {
            lock (_gate)
            {
                return _reasoningCache ??= _reasoning.ToString();
            }
        }
    }

    /// <summary>
    /// Gets the tool calls the model made in this reply, as a snapshot that stays valid while calls are added.
    /// </summary>
    public IReadOnlyList<ToolCallEntry> ToolCalls => Volatile.Read(ref _toolCalls);

    /// <summary>
    /// Gets or sets when the message was sent, or for a reply when it began, and when it finished once it has.
    /// </summary>
    public DateTimeOffset At
    {
        get => new(Volatile.Read(ref _atTicks), TimeSpan.Zero);
        set => Volatile.Write(ref _atTicks, value.UtcTicks);
    }

    /// <summary>
    /// Gets or sets the files attached to a user message, and how each reached the model.
    /// </summary>
    public IReadOnlyList<SentAttachment> Attachments { get; set; } = [];

    /// <summary>
    /// Gets or sets the state of the message.
    /// </summary>
    public EntryStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the error message when <see cref="Status"/> is <see cref="EntryStatus.Failed"/>.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets a label for the provider, model and level that produced the reply.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Gets or sets the token usage the server reported for the reply.
    /// </summary>
    public UsageReported? Usage { get; set; }

    /// <summary>
    /// Gets or sets what this entry stands for when it is a compaction marker, or null for a message.
    /// </summary>
    /// <remarks>
    /// A marker sits between the messages that were summarized and the ones that are still sent as they were.
    /// It is never part of the history sent to the model, and its text is empty.
    /// </remarks>
    public CompactionInfo? Compaction { get; set; }

    /// <summary>
    /// Gets or sets the length of the model-facing history before this message, so a retry or an edit can drop it.
    /// </summary>
    public int HistoryIndex { get; set; }

    /// <summary>
    /// Add a tool call the model made in this reply.
    /// </summary>
    /// <param name="call">The call to add.</param>
    public void AddToolCall(ToolCallEntry call)
    {
        ArgumentNullException.ThrowIfNull(call);
        lock (_gate)
        {
            _toolCalls = [.. _toolCalls, call];
        }
    }

    /// <summary>
    /// Append streamed reply text.
    /// </summary>
    /// <param name="text">The text to append.</param>
    public void AppendText(string text)
    {
        lock (_gate)
        {
            _text.Append(text);
            _textCache = null;
        }
    }

    /// <summary>
    /// Append streamed thinking text.
    /// </summary>
    /// <param name="text">The text to append.</param>
    public void AppendReasoning(string text)
    {
        lock (_gate)
        {
            _reasoning.Append(text);
            _reasoningCache = null;
        }
    }
}
