namespace BlazeHarness.Core.Providers;

/// <summary>
/// Describes one sampling parameter: its names, valid range and how to read it.
/// </summary>
/// <param name="Key">The camel-case name used in config paths and validation messages, for example <c>topP</c>.</param>
/// <param name="WireName">The snake-case name used in request bodies and pasted JSON, for example <c>top_p</c>.</param>
/// <param name="Label">The name shown to the user.</param>
/// <param name="Hint">A short explanation for the user.</param>
/// <param name="Min">The smallest valid value.</param>
/// <param name="Max">The largest valid value.</param>
/// <param name="SliderMax">The largest value a slider offers; larger values can still be typed.</param>
/// <param name="Step">The slider and number field step.</param>
/// <param name="IsInteger">Whether only whole numbers are valid.</param>
/// <param name="Get">Reads the value from a set of sampling values.</param>
public sealed record SamplingRange(
    string Key,
    string WireName,
    string Label,
    string Hint,
    double Min,
    double Max,
    double SliderMax,
    double Step,
    bool IsInteger,
    Func<SamplingParams, double?> Get)
{
    /// <summary>
    /// Gets every sampling parameter the app knows, in display order.
    /// </summary>
    public static IReadOnlyList<SamplingRange> All { get; } =
    [
        new("temperature", "temperature", "Temperature", "Higher values give more varied output.", 0, 2, 2, 0.05, false, s => s.Temperature),
        new("topP", "top_p", "Top P", "Sample only from the most likely tokens that add up to this probability.", 0, 1, 1, 0.01, false, s => s.TopP),
        new("topK", "top_k", "Top K", "Sample only from this many of the most likely tokens. Not part of the OpenAI API.", 0, int.MaxValue, 200, 1, true, s => s.TopK),
        new("minP", "min_p", "Min P", "Drop tokens less likely than this share of the best token. Not part of the OpenAI API.", 0, 1, 1, 0.01, false, s => s.MinP),
        new("presencePenalty", "presence_penalty", "Presence penalty", "Discourage tokens that already appeared.", -2, 2, 2, 0.1, false, s => s.PresencePenalty),
        new("frequencyPenalty", "frequency_penalty", "Frequency penalty", "Discourage tokens in proportion to how often they appeared.", -2, 2, 2, 0.1, false, s => s.FrequencyPenalty),
        new("repetitionPenalty", "repetition_penalty", "Repetition penalty", "Multiplicative penalty for repeats; 1 means none. Not part of the OpenAI API.", 0.01, 2, 2, 0.01, false, s => s.RepetitionPenalty),
    ];
}
