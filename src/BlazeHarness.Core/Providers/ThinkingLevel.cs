namespace BlazeHarness.Core.Providers;

/// <summary>
/// How much a reasoning model should think before answering.
/// </summary>
/// <remarks>
/// A model only supports the levels listed in its <see cref="ModelConfig.Levels"/> table.
/// </remarks>
public enum ThinkingLevel
{
    /// <summary>No thinking.</summary>
    Off,

    /// <summary>The smallest amount of thinking.</summary>
    Minimal,

    /// <summary>Low effort.</summary>
    Low,

    /// <summary>Medium effort.</summary>
    Medium,

    /// <summary>High effort.</summary>
    High,

    /// <summary>The highest effort.</summary>
    Xhigh,
}
