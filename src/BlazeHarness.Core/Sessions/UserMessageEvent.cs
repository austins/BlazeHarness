using BlazeHarness.Core.Attachments;

namespace BlazeHarness.Core.Sessions;

/// <summary>
/// The user sent a message.
/// </summary>
/// <param name="At">When the message was sent.</param>
/// <param name="EntryId">The id of the message.</param>
/// <param name="Text">The message text.</param>
/// <param name="Attachments">The attached files, or null when there are none.</param>
internal sealed record UserMessageEvent(DateTimeOffset At, Guid EntryId, string Text, IReadOnlyList<SentAttachment>? Attachments = null) : SessionEvent(At);
