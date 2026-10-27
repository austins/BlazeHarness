namespace BlazeHarness.Core.Sessions;

/// <summary>
/// Messages after one message were dropped, as when a reply is retried. They stay in the file but not in the conversation.
/// </summary>
/// <param name="At">When the rewind happened.</param>
/// <param name="ToEntryId">The last message to keep, or <see cref="Guid.Empty"/> to keep none.</param>
public sealed record RewindEvent(DateTimeOffset At, Guid ToEntryId) : SessionEvent(At);
