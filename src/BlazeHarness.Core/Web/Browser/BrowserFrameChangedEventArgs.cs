namespace BlazeHarness.Core.Web.Browser;

/// <summary>
/// Describes a change of what a chat's browser shows.
/// </summary>
public sealed class BrowserFrameChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserFrameChangedEventArgs"/> class.
    /// </summary>
    /// <param name="chatId">The id of the chat whose browser changed.</param>
    /// <param name="frame">The new picture, or null when the chat's page was closed.</param>
    public BrowserFrameChangedEventArgs(string chatId, BrowserFrame? frame)
    {
        ArgumentException.ThrowIfNullOrEmpty(chatId);
        ChatId = chatId;
        Frame = frame;
    }

    /// <summary>
    /// Gets the id of the chat whose browser changed.
    /// </summary>
    public string ChatId { get; }

    /// <summary>
    /// Gets the new picture, or null when the chat's page was closed.
    /// </summary>
    public BrowserFrame? Frame { get; }
}
