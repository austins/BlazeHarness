namespace BlazeHarness.Core.Providers;

/// <summary>
/// One entry of a model's <c>levels</c> table.
/// </summary>
/// <remarks>
/// A level that is absent from the table is unsupported by the model.
/// </remarks>
public sealed record LevelConfig
{
    /// <summary>
    /// Gets the provider's value for this level (e.g. "none", "high", "true"); null sends nothing.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>
    /// Gets sampling values that apply only while this level is selected.
    /// </summary>
    public SamplingParams? Overrides { get; init; }
}
