using BlazeHarness.Core.Tools;
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
    /// Estimate the tokens the definitions of some tools take in a request.
    /// </summary>
    /// <param name="tools">The tools the model is offered.</param>
    /// <returns>The estimate, rounded up.</returns>
    public static long OfTools(IEnumerable<ITool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        return OfCharacters(tools.Sum(t => t.Name.Length + t.Description.Length + t.InputSchema.Length));
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
    public static long Of(ChatMessage message) => Of(message, includeReasoning: true);

    /// <summary>
    /// Estimate the tokens in a message, including its tool calls, tool results and images.
    /// </summary>
    /// <param name="message">The message to measure.</param>
    /// <param name="includeReasoning">True to count the message's thinking; false when the request leaves it out.</param>
    /// <returns>The estimate, rounded up.</returns>
    public static long Of(ChatMessage message, bool includeReasoning)
    {
        ArgumentNullException.ThrowIfNull(message);

        var total = (long)MessageOverheadTokens;
        foreach (var content in message.Contents)
        {
            total += content switch
            {
                TextContent text => Of(text.Text),
                TextReasoningContent reasoning => includeReasoning ? Of(reasoning.Text) : 0,
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

    /// <summary>
    /// Estimate the tokens that messages add to a request, counting thinking only where the request sends it.
    /// </summary>
    /// <remarks>
    /// Thinking is sent back to the server only for the messages after the last user message, so older thinking costs nothing.
    /// </remarks>
    /// <param name="messages">The messages of the request.</param>
    /// <param name="from">The index of the first message to count.</param>
    /// <returns>The sum of the estimates for the messages from that index on.</returns>
    public static long OfRequest(IList<ChatMessage> messages, int from = 0)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var lastUser = -1;
        for (var i = 0; i < messages.Count; i++)
        {
            lastUser = messages[i].Role == ChatRole.User ? i : lastUser;
        }

        long total = 0;
        for (var i = Math.Max(0, from); i < messages.Count; i++)
        {
            total += Of(messages[i], includeReasoning: i > lastUser);
        }

        return total;
    }
}
