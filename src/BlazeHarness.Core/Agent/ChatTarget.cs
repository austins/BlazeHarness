using BlazeHarness.Core.Providers;
using System.Text.Json;

namespace BlazeHarness.Core.Agent;

/// <summary>
/// The provider, model and thinking level a message is sent with.
/// </summary>
/// <param name="Provider">The provider to call.</param>
/// <param name="Model">The model to use.</param>
/// <param name="Level">The thinking level, or null to send none so the server default applies.</param>
public sealed record ChatTarget(ProviderConfig Provider, ModelConfig Model, ThinkingLevel? Level)
{
    /// <summary>
    /// Gets a short label for the target, such as "DeepSeek · deepseek-chat · high".
    /// </summary>
    public string Label
    {
        get
        {
            var label = Provider.DisplayName + " · " + Model.Name;
            return Level is { } level ? label + " · " + JsonNamingPolicy.CamelCase.ConvertName(level.ToString()) : label;
        }
    }
}
