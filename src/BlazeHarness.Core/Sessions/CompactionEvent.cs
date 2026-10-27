namespace BlazeHarness.Core.Sessions;

/// <summary>
/// Older messages were replaced by a summary when the conversation is sent to the model. The messages themselves stay in the log.
/// </summary>
/// <param name="At">When the conversation was compacted.</param>
/// <param name="EntryId">The id of the marker that shows it in the conversation.</param>
/// <param name="FirstKeptEntryId">The first message that is still sent as it was; the marker sits just before it.</param>
/// <param name="Summary">The summary of every message before that one.</param>
/// <param name="TokensBefore">The estimated size of the conversation before compacting.</param>
/// <param name="TokensAfter">The estimated size of the conversation after compacting.</param>
public sealed record CompactionEvent(DateTimeOffset At, Guid EntryId, Guid FirstKeptEntryId, string Summary, long TokensBefore, long TokensAfter) : SessionEvent(At);
