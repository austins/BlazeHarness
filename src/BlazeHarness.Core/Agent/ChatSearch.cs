namespace BlazeHarness.Core.Agent;

/// <summary>
/// Finds the messages of a conversation that contain some text, for the find bar of an open chat.
/// </summary>
public static class ChatSearch
{
    /// <summary>
    /// Finds the messages that contain the text, ignoring case.
    /// </summary>
    /// <remarks>
    /// A message matches on its text, or on the name or arguments of one of its tool calls. Thinking text and
    /// tool output are not searched.
    /// </remarks>
    /// <param name="entries">The messages of the conversation, oldest first.</param>
    /// <param name="query">The text to look for; it is trimmed, and a null or blank query matches nothing.</param>
    /// <returns>The ids of the matching messages, oldest first.</returns>
    public static IReadOnlyList<Guid> Find(IReadOnlyList<ChatEntry> entries, string? query)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var text = query?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        return [.. entries.Where(entry => Matches(entry, text)).Select(entry => entry.Id)];
    }

    private static bool Matches(ChatEntry entry, string text)
    {
        return entry.Text.Contains(text, StringComparison.OrdinalIgnoreCase)
            || entry.ToolCalls.Any(call => call.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
            || call.ArgumentsJson.Contains(text, StringComparison.OrdinalIgnoreCase));
    }
}
