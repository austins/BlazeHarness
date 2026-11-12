using BlazeHarness.Core.Providers;
using BlazeHarness.Web.Resources;

namespace BlazeHarness.Web.Settings;

/// <summary>
/// The names shown for each way of sending the thinking level, shared by the provider and model editors.
/// </summary>
internal static class ThinkingSendLabels
{
    /// <summary>
    /// Gets the short name of a mode, for example <c>reasoning_effort</c>.
    /// </summary>
    /// <param name="mode">The mode to name.</param>
    /// <returns>The short name.</returns>
    public static string Short(ThinkingSendMode mode) => mode switch
    {
        ThinkingSendMode.ReasoningEffort => "reasoning_effort",
        ThinkingSendMode.ReasoningObject => WebStrings.ThinkingSendLabelsReasoningObjectShort,
        _ => WebStrings.ThinkingSendLabelsCustomFieldShort,
    };

    /// <summary>
    /// Gets the localized display name of a thinking level.
    /// </summary>
    /// <param name="level">The thinking level.</param>
    /// <returns>The localized name.</returns>
    public static string Level(ThinkingLevel level) => level switch
    {
        ThinkingLevel.Off => WebStrings.ThinkingLevelOff,
        ThinkingLevel.Minimal => WebStrings.ThinkingLevelMinimal,
        ThinkingLevel.Low => WebStrings.ThinkingLevelLow,
        ThinkingLevel.Medium => WebStrings.ThinkingLevelMedium,
        ThinkingLevel.High => WebStrings.ThinkingLevelHigh,
        _ => WebStrings.ThinkingLevelExtraHigh,
    };

    /// <summary>
    /// Gets the name of a mode with the servers that expect it.
    /// </summary>
    /// <param name="mode">The mode to name.</param>
    /// <returns>The name for a select option.</returns>
    public static string Option(ThinkingSendMode mode) => mode switch
    {
        ThinkingSendMode.ReasoningEffort => "reasoning_effort (OpenAI, llama.cpp, vLLM)",
        ThinkingSendMode.ReasoningObject => WebStrings.ThinkingSendLabelsReasoningObjectOption,
        _ => WebStrings.ThinkingSendLabelsCustomFieldOption,
    };
}
