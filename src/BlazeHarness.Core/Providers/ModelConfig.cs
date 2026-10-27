namespace BlazeHarness.Core.Providers;

/// <summary>
/// A model offered by a provider, with its limits and defaults.
/// </summary>
public sealed record ModelConfig
{
    /// <summary>
    /// Gets the model identifier sent to the provider.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the name shown in the UI, or null to show <see cref="Id"/>.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Gets the total tokens the model accepts.
    /// </summary>
    public int ContextWindow { get; init; }

    /// <summary>
    /// Gets the cap on a single response.
    /// </summary>
    public int MaxOutputTokens { get; init; }

    /// <summary>
    /// Gets a value indicating whether the model produces reasoning output.
    /// </summary>
    public bool Reasoning { get; init; }

    /// <summary>
    /// Gets the kinds of input the model accepts.
    /// </summary>
    public InputTypes Input { get; init; } = InputTypes.Text;

    /// <summary>
    /// Gets the model's default sampling values.
    /// </summary>
    public SamplingParams? SamplingParams { get; init; }

    /// <summary>
    /// Gets the supported thinking levels and how each one is sent, or null when thinking is not configurable.
    /// </summary>
    public IReadOnlyDictionary<ThinkingLevel, LevelConfig>? Levels { get; init; }

    /// <summary>
    /// Gets the thinking wire format for this model, overriding the provider's.
    /// </summary>
    public ThinkingSend? ThinkingSend { get; init; }

    /// <summary>
    /// Gets the name to show in the UI.
    /// </summary>
    public string Name => string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName;
}
