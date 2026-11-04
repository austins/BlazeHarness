namespace BlazeHarness.Core.Chats;

/// <summary>
/// The text that points at a chat, which a user can paste into a message for the model to act on.
/// </summary>
public static class ChatReference
{
    /// <summary>
    /// The start of every reference.
    /// </summary>
    public const string Prefix = "chat:";

    /// <summary>
    /// Build the reference to a chat.
    /// </summary>
    /// <param name="chatId">The chat id.</param>
    /// <returns>The reference, such as <c>chat:0123…</c>.</returns>
    public static string Format(string chatId)
    {
        ArgumentNullException.ThrowIfNull(chatId);
        return Prefix + chatId;
    }

    /// <summary>
    /// Read the chat id from a reference, or from a bare id.
    /// </summary>
    /// <param name="text">The text a person or the model gave.</param>
    /// <returns>The chat id without the prefix.</returns>
    public static string IdOf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();
        return trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ? trimmed[Prefix.Length..].Trim() : trimmed;
    }
}
