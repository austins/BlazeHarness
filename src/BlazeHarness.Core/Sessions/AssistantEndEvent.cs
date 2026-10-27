using BlazeHarness.Core.Agent;

namespace BlazeHarness.Core.Sessions;

/// <summary>
/// A reply ended. A reply with no end event was interrupted, for example by the app stopping.
/// A later end event for the same reply replaces the status, as when the user stops the tool calls of a reply that had already finished streaming.
/// </summary>
/// <param name="At">When the reply ended.</param>
/// <param name="EntryId">The id of the reply that ended.</param>
/// <param name="Status">How it ended.</param>
/// <param name="Error">The error message when it failed.</param>
/// <param name="Usage">The token usage the server reported.</param>
public sealed record AssistantEndEvent(DateTimeOffset At, Guid EntryId, EntryStatus Status, string? Error, UsageReported? Usage) : SessionEvent(At);
