namespace BlazeHarness.Web.Services;

/// <summary>
/// Keeps the side panel state of recently used chats while the app runs, so it survives switching chats and reloading the page.
/// </summary>
/// <remarks>
/// Nothing is written to disk: the changes are read from the folder again anyway, and the state only matters within a session.
/// Only the most recently used chats are kept, which also takes care of chats that were deleted.
/// Two windows open on the same chat share its state, as they share the chat.
/// </remarks>
internal sealed class SidePanelMemory
{
    /// <summary>The number of chats whose panel state is kept.</summary>
    public const int Capacity = 50;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, (ChatPanelMemory Memory, long Used)> _chats = new(StringComparer.Ordinal);
    private long _clock;

    /// <summary>
    /// Gets the remembered state of a chat, starting an empty one the first time.
    /// </summary>
    /// <param name="chatId">The id of the chat.</param>
    /// <returns>The chat's panel state, the same instance on every call while it is kept.</returns>
    public ChatPanelMemory For(string chatId)
    {
        lock (_lock)
        {
            var memory = _chats.TryGetValue(chatId, out var entry) ? entry.Memory : new ChatPanelMemory();
            Store(chatId, memory);
            return memory;
        }
    }

    /// <summary>
    /// Keeps a state under a chat id, as when a new chat gets its id with its first message.
    /// </summary>
    /// <param name="chatId">The id of the chat.</param>
    /// <param name="memory">The panel state the chat had before it had an id.</param>
    public void Adopt(string chatId, ChatPanelMemory memory)
    {
        lock (_lock)
        {
            Store(chatId, memory);
        }
    }

    private void Store(string chatId, ChatPanelMemory memory)
    {
        _chats[chatId] = (memory, ++_clock);
        if (_chats.Count > Capacity)
        {
            _chats.Remove(_chats.MinBy(pair => pair.Value.Used).Key);
        }
    }
}
