using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Agent;

/// <summary>
/// Builds the user messages the runner adds within a step, such as the pictures of tool calls, which are not a new request from the user.
/// </summary>
/// <remarks>
/// The tags make chat templates such as Qwen's read the message as a tool result rather than a new request,
/// which would drop the thinking of the earlier steps and with it the server's prompt cache.
/// </remarks>
internal static class StepMessage
{
    private const string Start = "<tool_response>\n";
    private const string End = "\n</tool_response>";

    /// <summary>
    /// Create a step message.
    /// </summary>
    /// <param name="contents">What the message carries.</param>
    /// <returns>The message.</returns>
    public static ChatMessage Create(IEnumerable<AIContent> contents)
    {
        return new ChatMessage(ChatRole.User, [new TextContent(Start), .. contents, new TextContent(End)]);
    }

    /// <summary>
    /// Get whether a message is one that <see cref="Create"/> built.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>True when the message is part of a step rather than a request from the user.</returns>
    public static bool Is(ChatMessage message)
    {
        return message.Role == ChatRole.User && message.Contents is [TextContent { Text: Start }, ..];
    }
}
