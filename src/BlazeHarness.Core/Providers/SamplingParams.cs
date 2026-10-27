using System.Text.Json;

namespace BlazeHarness.Core.Providers;

/// <summary>
/// Sampling settings for a request.
/// </summary>
/// <remarks>
/// All values are optional: an unset value is omitted from the request so the server default applies.
/// </remarks>
public sealed record SamplingParams
{
    /// <summary>
    /// Gets the sampling temperature.
    /// </summary>
    public double? Temperature { get; init; }

    /// <summary>
    /// Gets the nucleus sampling probability mass.
    /// </summary>
    public double? TopP { get; init; }

    /// <summary>
    /// Gets how many of the most likely tokens to sample from. Not part of the OpenAI API; sent as a body field.
    /// </summary>
    public int? TopK { get; init; }

    /// <summary>
    /// Gets the minimum token probability relative to the most likely token. Not part of the OpenAI API; sent as a body field.
    /// </summary>
    public double? MinP { get; init; }

    /// <summary>
    /// Gets the penalty for tokens that already appeared at all.
    /// </summary>
    public double? PresencePenalty { get; init; }

    /// <summary>
    /// Gets the penalty that grows with how often a token already appeared.
    /// </summary>
    public double? FrequencyPenalty { get; init; }

    /// <summary>
    /// Gets the multiplicative repetition penalty. Not part of the OpenAI API; sent as a body field.
    /// </summary>
    public double? RepetitionPenalty { get; init; }

    /// <summary>
    /// Gets server-specific extra body fields.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>
    /// Layer another set of values over this one.
    /// </summary>
    /// <param name="overrides">The values that win where set, or null to keep this instance unchanged.</param>
    /// <returns>This instance with every value set on <paramref name="overrides"/> replacing its own.</returns>
    public SamplingParams MergeOver(SamplingParams? overrides)
    {
        if (overrides is null)
        {
            return this;
        }

        Dictionary<string, JsonElement>? extra = null;
        if (Extra is not null || overrides.Extra is not null)
        {
            extra = Extra is null ? [] : new Dictionary<string, JsonElement>(Extra);
            if (overrides.Extra is not null)
            {
                foreach (var (key, value) in overrides.Extra)
                {
                    extra[key] = value;
                }
            }
        }

        return new SamplingParams
        {
            Temperature = overrides.Temperature ?? Temperature,
            TopP = overrides.TopP ?? TopP,
            TopK = overrides.TopK ?? TopK,
            MinP = overrides.MinP ?? MinP,
            PresencePenalty = overrides.PresencePenalty ?? PresencePenalty,
            FrequencyPenalty = overrides.FrequencyPenalty ?? FrequencyPenalty,
            RepetitionPenalty = overrides.RepetitionPenalty ?? RepetitionPenalty,
            Extra = extra,
        };
    }
}
