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
