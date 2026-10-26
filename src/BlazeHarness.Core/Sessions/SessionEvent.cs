using BlazeHarness.Core.Agent;
using System.Text.Json.Serialization;

namespace BlazeHarness.Core.Sessions;

/// <summary>
/// One line of a session's <c>messages.jsonl</c>.
/// </summary>
/// <remarks>
/// The log is append-only. Streamed replies are written as a start, batched deltas and an end,
/// and a rewind marks earlier messages as dropped without deleting them.
/// </remarks>
/// <param name="At">When the event was written.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(UserMessageEvent), "user")]
[JsonDerivedType(typeof(AssistantStartEvent), "assistantStart")]
[JsonDerivedType(typeof(AssistantDeltaEvent), "assistantDelta")]
[JsonDerivedType(typeof(AssistantEndEvent), "assistantEnd")]
[JsonDerivedType(typeof(RewindEvent), "rewind")]
public abstract record SessionEvent(DateTimeOffset At);

/// <summary>
/// The user sent a message.
/// </summary>
/// <param name="At">When the message was sent.</param>
/// <param name="EntryId">The id of the message.</param>
/// <param name="Text">The message text.</param>
public sealed record UserMessageEvent(DateTimeOffset At, Guid EntryId, string Text) : SessionEvent(At);

/// <summary>
/// The model began a reply.
/// </summary>
/// <param name="At">When the reply began.</param>
/// <param name="EntryId">The id of the reply.</param>
/// <param name="Label">A label for the provider, model and level that produced it.</param>
public sealed record AssistantStartEvent(DateTimeOffset At, Guid EntryId, string? Label) : SessionEvent(At);

/// <summary>
/// A batch of streamed reply text.
/// </summary>
/// <param name="At">When the batch was written.</param>
/// <param name="EntryId">The id of the reply the text belongs to.</param>
/// <param name="Text">Visible text to append, or an empty string.</param>
/// <param name="Reasoning">Thinking text to append, or an empty string.</param>
public sealed record AssistantDeltaEvent(DateTimeOffset At, Guid EntryId, string Text, string Reasoning) : SessionEvent(At);

/// <summary>
/// A reply ended. A reply with no end event was interrupted, for example by the app stopping.
/// </summary>
/// <param name="At">When the reply ended.</param>
/// <param name="EntryId">The id of the reply that ended.</param>
/// <param name="Status">How it ended.</param>
/// <param name="Error">The error message when it failed.</param>
/// <param name="Usage">The token usage the server reported.</param>
public sealed record AssistantEndEvent(DateTimeOffset At, Guid EntryId, EntryStatus Status, string? Error, UsageReported? Usage) : SessionEvent(At);

/// <summary>
/// Messages after one message were dropped, as when a reply is retried. They stay in the file but not in the conversation.
/// </summary>
/// <param name="At">When the rewind happened.</param>
/// <param name="ToEntryId">The last message to keep.</param>
public sealed record RewindEvent(DateTimeOffset At, Guid ToEntryId) : SessionEvent(At);
