using Microsoft.Extensions.AI;
using System.Text.Json;

namespace BlazeHarness.Core.Compaction;

/// <summary>
/// A rough count of tokens from text length, for the parts of a conversation the server has not counted.
/// </summary>
/// <remarks>
/// There is no tokenizer in the app. The estimate is about four characters per token, which is close enough
/// to decide when to compact and is always shown as approximate.
/// </remarks>
public static class TokenEstimate
{
    /// <summary>
    /// The characters counted as one token.
    /// </summary>
    public const int CharactersPerToken = 4;

    private const int ImageTokens = 1_000;
    private const int MessageOverheadTokens = 4;

    /// <summary>
    /// Estimate the tokens in a number of characters.
    /// </summary>
    /// <param name="characters">The number of characters.</param>
    /// <returns>The estimate, rounded up.</returns>
    public static long OfCharacters(int characters)
    {
        return (characters + CharactersPerToken - 1) / CharactersPerToken;
    }

    /// <summary>
    /// Estimate the tokens in a piece of text.
    /// </summary>
    /// <param name="text">The text to measure.</param>
    /// <returns>The estimate, rounded up.</returns>
    public static long Of(string? text)
    {
        return string.IsNullOrEmpty(text) ? 0 : OfCharacters(text.Length);
    }

    /// <summary>
    /// Estimate the tokens in a message, including its tool calls, tool results and images.
    /// </summary>
    /// <param name="message">The message to measure.</param>
    /// <returns>The estimate, rounded up.</returns>
    public static long Of(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var total = (long)MessageOverheadTokens;
        foreach (var content in message.Contents)
        {
            total += content switch
            {
                TextContent text => Of(text.Text),
                FunctionCallContent call => Of(call.Name) + Of(JsonSerializer.Serialize(call.Arguments, AIJsonUtilities.DefaultOptions)),
                FunctionResultContent result => Of(result.Result?.ToString()),
                DataContent data when data.HasTopLevelMediaType("image") => ImageTokens,
                UriContent uri when uri.HasTopLevelMediaType("image") => ImageTokens,
                _ => 0,
            };
        }

        return total;
    }

    /// <summary>
    /// Estimate the tokens in a list of messages.
    /// </summary>
    /// <param name="messages">The messages to measure.</param>
    /// <returns>The sum of the estimates for the messages.</returns>
    public static long Of(IEnumerable<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return messages.Sum(Of);
    }
}
