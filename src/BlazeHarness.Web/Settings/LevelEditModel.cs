using BlazeHarness.Core.Providers;

namespace BlazeHarness.Web.Settings;

/// <summary>
/// A mutable copy of one thinking level for the editing form.
/// </summary>
public sealed class LevelEditModel
{
    /// <summary>
    /// Gets or sets a value indicating whether the model supports this level.
    /// </summary>
    public bool Supported { get; set; }

    /// <summary>
    /// Gets or sets the value sent to the provider for this level; empty sends nothing.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Create an editable copy of a level.
    /// </summary>
    /// <param name="level">The level to copy, or null for an unsupported level.</param>
    /// <returns>The editing model.</returns>
    public static LevelEditModel FromConfig(LevelConfig? level)
    {
        return level is null
            ? new LevelEditModel()
            : new LevelEditModel
            {
                Supported = true,
                Value = level.Value ?? string.Empty,
            };
    }

    /// <summary>
    /// Convert back to the stored form.
    /// </summary>
    /// <param name="overrides">The sampling values that apply while this level is selected.</param>
    /// <returns>The level, or null when the model does not support it.</returns>
    public LevelConfig? ToConfig(SamplingParams? overrides)
    {
        return Supported
            ? new LevelConfig
            {
                Value = string.IsNullOrWhiteSpace(Value) ? null : Value.Trim(),
                Overrides = overrides,
            }
            : null;
    }
}
