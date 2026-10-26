namespace BlazeHarness.Core.Providers;

/// <summary>
/// A ready-made sampling and thinking setup for a family of models.
/// </summary>
/// <param name="Id">A stable identifier.</param>
/// <param name="Name">The name shown to the user.</param>
/// <param name="Description">What the preset sets and which servers it suits.</param>
/// <param name="Sampling">The default sampling values, or null to leave them unset.</param>
/// <param name="Levels">The supported thinking levels.</param>
/// <param name="ThinkingSend">How levels go on the wire, or null to use the provider's setting.</param>
public sealed record ThinkingPreset(
    string Id,
    string Name,
    string Description,
    SamplingParams? Sampling,
    IReadOnlyDictionary<ThinkingLevel, LevelConfig> Levels,
    ThinkingSend? ThinkingSend)
{
    /// <summary>
    /// Gets every preset, in display order.
    /// </summary>
    public static IReadOnlyList<ThinkingPreset> All { get; } =
    [
        new(
            "openai-effort",
            "OpenAI reasoning effort",
            "Minimal, low, medium and high, sent as reasoning_effort.",
            null,
            new Dictionary<ThinkingLevel, LevelConfig>
            {
                [ThinkingLevel.Minimal] = new() { Value = "minimal" },
                [ThinkingLevel.Low] = new() { Value = "low" },
                [ThinkingLevel.Medium] = new() { Value = "medium" },
                [ThinkingLevel.High] = new() { Value = "high" },
            },
            new ThinkingSend { Mode = ThinkingSendMode.ReasoningEffort }),
        new(
            "effort-with-off",
            "Reasoning effort with off",
            "Off, low, medium and high, sent as reasoning_effort, where off is \"none\". Suits Gemini's OpenAI-compatible API.",
            null,
            new Dictionary<ThinkingLevel, LevelConfig>
            {
                [ThinkingLevel.Off] = new() { Value = "none" },
                [ThinkingLevel.Low] = new() { Value = "low" },
                [ThinkingLevel.Medium] = new() { Value = "medium" },
                [ThinkingLevel.High] = new() { Value = "high" },
            },
            new ThinkingSend { Mode = ThinkingSendMode.ReasoningEffort }),
        new(
            "qwen3",
            "Qwen3 thinking on or off",
            "Qwen's recommended settings, with thinking switched by chat_template_kwargs.enable_thinking. Suits vLLM, llama.cpp and SGLang.",
            new SamplingParams { Temperature = 0.6, TopP = 0.95, TopK = 20, MinP = 0 },
            new Dictionary<ThinkingLevel, LevelConfig>
            {
                [ThinkingLevel.Off] = new()
                {
                    Value = "false",
                    Overrides = new SamplingParams { Temperature = 0.7, TopP = 0.8, TopK = 20, MinP = 0 },
                },
                [ThinkingLevel.High] = new() { Value = "true" },
            },
            new ThinkingSend { Mode = ThinkingSendMode.BodyField, Field = "chat_template_kwargs.enable_thinking" }),
    ];

    /// <summary>
    /// Find a preset by id.
    /// </summary>
    /// <param name="id">The preset id.</param>
    /// <returns>The preset, or null when none has that id.</returns>
    public static ThinkingPreset? Find(string id)
    {
        return All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
    }
}
