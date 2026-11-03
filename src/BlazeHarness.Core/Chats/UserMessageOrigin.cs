namespace BlazeHarness.Core.Chats;

/// <summary>
/// Why a user message was added to a chat other than by the user starting a turn.
/// </summary>
/// <remarks>
/// A message without an origin was sent by the user to start a turn. The value is saved by name, so a name must not change.
/// A name this version does not know, from a chat saved by a newer version, is read as <see cref="Unknown"/>.
/// </remarks>
public enum UserMessageOrigin
{
    /// <summary>
    /// The user sent it while a reply was running, and it was added to the conversation between two model calls.
    /// </summary>
    Steer,

    /// <summary>
    /// Added for a reason a newer version of the app knows and this one does not. It is never saved by this version.
    /// </summary>
    Unknown,
}
