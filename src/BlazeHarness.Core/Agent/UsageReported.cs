using System.Text.Json.Serialization;

namespace BlazeHarness.Core.Agent;

/// <summary>
/// Token counts as reported by the endpoint, with how long the response took.
/// </summary>
/// <param name="PromptTokens">The prompt size, which is also the current context size.</param>
/// <param name="CompletionTokens">The number of tokens the model generated, including thinking.</param>
/// <param name="CachedTokens">The part of the prompt served from the provider's prompt cache, or 0 when not reported.</param>
/// <param name="ReasoningTokens">The part of <paramref name="CompletionTokens"/> spent thinking, or 0 when not reported.</param>
/// <param name="TimeToFirstToken">How long the request took to produce its first output, or null when not measured.</param>
/// <param name="GenerationTime">How long the output took to stream after the first part arrived, or null when not measured.</param>
public sealed record UsageReported(
    long PromptTokens,
    long CompletionTokens,
    long CachedTokens,
    long ReasoningTokens = 0,
    TimeSpan? TimeToFirstToken = null,
    TimeSpan? GenerationTime = null) : AgentEvent
{
    /// <summary>
    /// Gets the prompt tokens the server had to process, which excludes the cached part.
    /// </summary>
    [JsonIgnore]
    public long UncachedTokens => Math.Max(0, PromptTokens - CachedTokens);

    /// <summary>
    /// Gets how many uncached prompt tokens were processed per second before the first output, or null when it can't be told.
    /// </summary>
    [JsonIgnore]
    public double? PrefillTokensPerSecond => Rate(UncachedTokens, TimeToFirstToken);

    /// <summary>
    /// Gets how many tokens were generated per second after the first output, or null when it can't be told.
    /// </summary>
    /// <remarks>
    /// The first token arrives with the end of the prefill, so it is left out of the count.
    /// </remarks>
    [JsonIgnore]
    public double? DecodeTokensPerSecond => Rate(CompletionTokens - 1, GenerationTime);

    private static double? Rate(long tokens, TimeSpan? time)
    {
        return tokens > 0 && time is { TotalSeconds: > 0 } t ? tokens / t.TotalSeconds : null;
    }
}
