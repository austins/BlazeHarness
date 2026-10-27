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
[JsonDerivedType(typeof(ToolCallEvent), "toolCall")]
[JsonDerivedType(typeof(ToolResultEvent), "toolResult")]
[JsonDerivedType(typeof(RewindEvent), "rewind")]
[JsonDerivedType(typeof(PermissionsChangedEvent), "permissionsChanged")]
[JsonDerivedType(typeof(CompactionEvent), "compaction")]
public abstract record SessionEvent(DateTimeOffset At);
