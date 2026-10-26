using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Providers;
using Microsoft.Extensions.AI;
using System.Text;

namespace BlazeHarness.Core.Sessions;

/// <summary>
/// Writes titles by asking the model the conversation already uses, with a small request of its own
/// so the chat's cached prefix is never touched.
/// </summary>
public sealed class ModelTitleGenerator : ITitleGenerator
{
    internal const int MaxTitleLength = 60;

    private const int MaxOutputTokens = 256;
    private const int MaxExcerptLength = 600;
    private const string Instruction =
        "Write a title of at most six words for the conversation below. Reply with only the title, in the language of the conversation.";

    private static readonly char[] EdgeCharacters = ['"', '\'', '“', '”', '‘', '’', '`', '*', '#', '.', ':', ';', ',', '!', '。'];

    private readonly Func<ChatTarget, CancellationToken, Task<IChatClient>> _clientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelTitleGenerator"/> class.
    /// </summary>
    /// <param name="clientFactory">Creates the chat client for a target.</param>
    public ModelTitleGenerator(Func<ChatTarget, CancellationToken, Task<IChatClient>> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        _clientFactory = clientFactory;
    }

    /// <inheritdoc />
    public async Task<string?> GenerateAsync(ChatTarget target, string userText, string replyText, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var capped = target.Model with { MaxOutputTokens = MaxOutputTokens };
        var level = target.Model.Levels?.ContainsKey(ThinkingLevel.Off) == true ? ThinkingLevel.Off : (ThinkingLevel?)null;
        var options = ChatClientFactory.BuildOptions(target.Provider, capped, level);
        List<ChatMessage> messages =
        [
            new(ChatRole.System, Instruction),
            new(ChatRole.User, "User: " + Excerpt(userText) + "\n\nAssistant: " + Excerpt(replyText)),
        ];

        using var client = await _clientFactory(target, cancellationToken).ConfigureAwait(false);
        var title = new StringBuilder();
        await foreach (var e in ChatRunner.RunAsync(client, messages, options, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            switch (e)
            {
                case TextDelta text:
                    title.Append(text.Text);
                    break;
                case AgentError error:
                    throw new InvalidOperationException(error.Message);
                default:
                    break;
            }
        }

        return Clean(title.ToString());
    }

    /// <summary>
    /// Tidy a model's answer into a title.
    /// </summary>
    /// <param name="raw">The answer as the model wrote it.</param>
    /// <returns>The first line without quotes, markup or end punctuation and cut to a sensible length, or null when nothing is left.</returns>
    internal static string? Clean(string raw)
    {
        var line = raw.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        if (line.StartsWith("Title:", StringComparison.OrdinalIgnoreCase))
        {
            line = line["Title:".Length..];
        }

        var words = string.Join(' ', line.Trim().Trim(EdgeCharacters).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (words.Length > MaxTitleLength)
        {
            var cut = char.IsHighSurrogate(words[MaxTitleLength - 1]) ? MaxTitleLength - 1 : MaxTitleLength;
            words = words[..cut].TrimEnd() + "…";
        }

        return words.Length == 0 ? null : words;
    }

    private static string Excerpt(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length <= MaxExcerptLength)
        {
            return trimmed;
        }

        var cut = char.IsHighSurrogate(trimmed[MaxExcerptLength - 1]) ? MaxExcerptLength - 1 : MaxExcerptLength;
        return trimmed[..cut] + "…";
    }
}
