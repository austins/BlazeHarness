using BlazeHarness.Core.Agent;

namespace BlazeHarness.Core.Sessions;

/// <summary>
/// Writes a short title for a conversation.
/// </summary>
public interface ITitleGenerator
{
    /// <summary>
    /// Write a title from the first exchange of a conversation.
    /// </summary>
    /// <param name="target">The provider, model and level the conversation used.</param>
    /// <param name="userText">The first user message.</param>
    /// <param name="replyText">The assistant's reply to it.</param>
    /// <param name="cancellationToken">A token that stops the request.</param>
    /// <returns>The title, or null when none could be made.</returns>
    Task<string?> GenerateAsync(ChatTarget target, string userText, string replyText, CancellationToken cancellationToken = default);
}
