namespace BlazeHarness.Core.Chats;

/// <summary>
/// Tells which chat a <see cref="ChatStore"/> change was about.
/// </summary>
public sealed class ChatStoreChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChatStoreChangedEventArgs"/> class.
    /// </summary>
    /// <param name="chatId">The id of the chat that was created, changed or deleted.</param>
    public ChatStoreChangedEventArgs(string chatId)
    {
        ArgumentNullException.ThrowIfNull(chatId);
        ChatId = chatId;
    }

    /// <summary>
    /// Gets the id of the chat that was created, changed or deleted.
    /// </summary>
    public string ChatId { get; }
}
