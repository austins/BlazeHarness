using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Compaction;

/// <summary>
/// Writes the summary that replaces older messages of a conversation.
/// </summary>
public interface ISummarizer
{
    /// <summary>
    /// Summarize messages.
    /// </summary>
    /// <param name="previousSummary">The summary of the messages before these, or null when there is none.</param>
    /// <param name="messages">The messages to fold into the summary, in order. Tool calls and results are always paired.</param>
    /// <param name="cancellationToken">A token that stops the request.</param>
    /// <returns>The new summary, which covers the previous summary as well.</returns>
    Task<string> SummarizeAsync(string? previousSummary, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken);
}
