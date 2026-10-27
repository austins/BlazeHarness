namespace BlazeHarness.Core.Providers;

/// <summary>
/// The result of reading a pasted sampling and thinking snippet.
/// </summary>
/// <param name="Sampling">The default sampling values, or null when the snippet had none.</param>
/// <param name="Levels">The supported thinking levels, or null when the snippet defined none.</param>
/// <param name="Errors">Problems found. The other values are only meaningful when this is empty.</param>
public sealed record SamplingImportResult(
    SamplingParams? Sampling,
    IReadOnlyDictionary<ThinkingLevel, LevelConfig>? Levels,
    IReadOnlyList<string> Errors);
