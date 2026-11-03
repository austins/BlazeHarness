using BlazeHarness.Core.Attachments;
using System.Text.Json.Serialization;

namespace BlazeHarness.Core.Chats.Events;

/// <summary>
/// The user sent a message.
/// </summary>
/// <param name="At">When the message was sent.</param>
/// <param name="EntryId">The id of the message.</param>
/// <param name="Text">The message text.</param>
/// <param name="Attachments">The attached files, or null when there are none.</param>
/// <param name="Origin">Why the message was added when the user did not start a turn with it, or null for a message that did. Logs from before it existed have none. A name this version does not know is read as <see cref="UserMessageOrigin.Unknown"/>.</param>
internal sealed record UserMessageEvent(DateTimeOffset At, Guid EntryId, string Text, IReadOnlyList<SentAttachment>? Attachments = null, [property: JsonConverter(typeof(UserMessageOriginConverter))] UserMessageOrigin? Origin = null) : ChatEvent(At);
