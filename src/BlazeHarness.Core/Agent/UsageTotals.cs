namespace BlazeHarness.Core.Agent;

/// <summary>
/// The usage of every response in a chat, added up.
/// </summary>
/// <param name="Responses">How many responses reported usage.</param>
/// <param name="PromptTokens">The prompt tokens sent, over all responses.</param>
/// <param name="CachedTokens">The part of <paramref name="PromptTokens"/> served from the provider's cache.</param>
/// <param name="CompletionTokens">The tokens generated, including thinking.</param>
/// <param name="ReasoningTokens">The part of <paramref name="CompletionTokens"/> spent thinking.</param>
/// <param name="PrefillTokensPerSecond">The uncached prompt tokens processed per second, or null when no response was timed.</param>
/// <param name="DecodeTokensPerSecond">The tokens generated per second, or null when no response was timed.</param>
/// <param name="TimeToFirstToken">The average wait for the first output, or null when no response was timed.</param>
/// <param name="GenerationTime">The time spent streaming output, over all responses.</param>
public sealed record UsageTotals(
    int Responses,
    long PromptTokens,
    long CachedTokens,
    long CompletionTokens,
    long ReasoningTokens,
    double? PrefillTokensPerSecond,
    double? DecodeTokensPerSecond,
    TimeSpan? TimeToFirstToken,
    TimeSpan GenerationTime)
{
    /// <summary>
    /// Gets the share of the prompt tokens that came from the cache, or null when nothing was sent.
    /// </summary>
    public double? CacheHitRate => PromptTokens > 0 ? (double)CachedTokens / PromptTokens : null;

    /// <summary>
    /// Add up the usage of the given responses.
    /// </summary>
    /// <remarks>
    /// A speed is the tokens of the timed responses over their time, so a long response counts for more than a short one.
    /// </remarks>
    /// <param name="usages">The usage of each response.</param>
    /// <returns>The totals.</returns>
    public static UsageTotals Of(IEnumerable<UsageReported> usages)
    {
        ArgumentNullException.ThrowIfNull(usages);

        var responses = 0;
        long prompt = 0, cached = 0, completion = 0, reasoning = 0, prefillTokens = 0, decodeTokens = 0;
        TimeSpan prefillTime = TimeSpan.Zero, decodeTime = TimeSpan.Zero;
        var firstTokens = 0;
        foreach (var usage in usages)
        {
            responses++;
            prompt += usage.PromptTokens;
            cached += usage.CachedTokens;
            completion += usage.CompletionTokens;
            reasoning += usage.ReasoningTokens;
            if (usage.TimeToFirstToken is { } wait)
            {
                prefillTokens += usage.UncachedTokens;
                prefillTime += wait;
                firstTokens++;
            }

            if (usage.GenerationTime is { } generation && usage.CompletionTokens > 1)
            {
                decodeTokens += usage.CompletionTokens - 1;
                decodeTime += generation;
            }
        }

        return new UsageTotals(
            responses,
            prompt,
            cached,
            completion,
            reasoning,
            prefillTime > TimeSpan.Zero && prefillTokens > 0 ? prefillTokens / prefillTime.TotalSeconds : null,
            decodeTime > TimeSpan.Zero && decodeTokens > 0 ? decodeTokens / decodeTime.TotalSeconds : null,
            firstTokens > 0 ? prefillTime / firstTokens : null,
            decodeTime);
    }
}
