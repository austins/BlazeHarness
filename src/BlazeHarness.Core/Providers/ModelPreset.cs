namespace BlazeHarness.Core.Providers;

/// <summary>
/// Known limits for a well-known model, used to prefill a model's settings.
/// </summary>
/// <param name="ContextWindow">Total tokens the model accepts.</param>
/// <param name="MaxOutputTokens">The cap on a single response.</param>
/// <param name="Reasoning">Whether the model produces reasoning output.</param>
/// <param name="Image">Whether the model accepts images.</param>
public sealed record ModelPreset(int ContextWindow, int MaxOutputTokens, bool Reasoning, bool Image)
{
    private static readonly (string Prefix, ModelPreset Preset)[] Known =
    [
        ("gpt-5", new ModelPreset(400_000, 128_000, Reasoning: true, Image: true)),
        ("gpt-4.1", new ModelPreset(1_047_576, 32_768, Reasoning: false, Image: true)),
        ("gpt-4o", new ModelPreset(128_000, 16_384, Reasoning: false, Image: true)),
        ("o3", new ModelPreset(200_000, 100_000, Reasoning: true, Image: true)),
        ("o4-mini", new ModelPreset(200_000, 100_000, Reasoning: true, Image: true)),
        ("deepseek-chat", new ModelPreset(128_000, 8_192, Reasoning: false, Image: false)),
        ("deepseek-reasoner", new ModelPreset(128_000, 65_536, Reasoning: true, Image: false)),
        ("gemini-2.5", new ModelPreset(1_048_576, 65_536, Reasoning: true, Image: true)),
        ("qwen3", new ModelPreset(131_072, 32_768, Reasoning: true, Image: false)),
    ];

    /// <summary>
    /// Find a preset for a model id.
    /// </summary>
    /// <remarks>
    /// Matching is by case-insensitive prefix, ignoring any <c>vendor/</c> prefix, so <c>openai/gpt-4o-mini</c> matches <c>gpt-4o</c>.
    /// The values are starting points that the user can edit; providers differ on the exact limits.
    /// </remarks>
    /// <param name="modelId">The model id as the provider lists it.</param>
    /// <returns>The preset, or null when the model is not known.</returns>
    public static ModelPreset? Find(string modelId)
    {
        var name = modelId[(modelId.LastIndexOf('/') + 1)..];
        return Known
            .Where(k => name.StartsWith(k.Prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(k => k.Prefix.Length)
            .Select(k => k.Preset)
            .FirstOrDefault();
    }
}
