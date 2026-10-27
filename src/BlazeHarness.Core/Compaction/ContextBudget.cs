using BlazeHarness.Core.Providers;

namespace BlazeHarness.Core.Compaction;

/// <summary>
/// How much conversation a model can hold before it is compacted.
/// </summary>
internal static class ContextBudget
{
    /// <summary>
    /// The part of the room left for the conversation, after the response, that may be used before compacting.
    /// </summary>
    public const double Threshold = 0.8;

    /// <summary>
    /// Get the context size at which the conversation is compacted.
    /// </summary>
    /// <remarks>
    /// It is the window minus the room kept for the response, times <see cref="Threshold"/>.
    /// A model with an unknown window is never compacted automatically.
    /// </remarks>
    /// <param name="model">The model the next request goes to.</param>
    /// <returns>The size in tokens, or 0 when the window is not known.</returns>
    public static int Limit(ModelConfig model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.ContextWindow <= 0)
        {
            return 0;
        }

        // An output cap that leaves no room for a prompt is a configuration mistake, so half the window is kept instead.
        var output = model.MaxOutputTokens > 0 && model.MaxOutputTokens < model.ContextWindow / 2 ? model.MaxOutputTokens : model.ContextWindow / 2;
        return Math.Max(1, (int)((model.ContextWindow - output) * Threshold));
    }
}
