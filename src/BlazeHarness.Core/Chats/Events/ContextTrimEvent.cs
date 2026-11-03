namespace BlazeHarness.Core.Chats.Events;

/// <summary>
/// Large tool output of older messages was replaced by a stub when the conversation is sent to the model. The output itself stays in the log.
/// </summary>
/// <param name="At">When the output was trimmed.</param>
/// <param name="EntryId">The id of the marker that shows it in the conversation.</param>
/// <param name="TrimmedBeforeEntryId">The first message whose tool output is still sent in full; the marker sits just before it.</param>
/// <param name="TokensBefore">The estimated size of the conversation before trimming.</param>
/// <param name="TokensAfter">The estimated size of the conversation after trimming.</param>
internal sealed record ContextTrimEvent(DateTimeOffset At, Guid EntryId, Guid TrimmedBeforeEntryId, long TokensBefore, long TokensAfter) : ChatEvent(At);
