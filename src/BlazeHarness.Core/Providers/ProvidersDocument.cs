using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazeHarness.Core.Providers;

/// <summary>
/// The contents of <c>providers.json</c>.
/// </summary>
public sealed record ProvidersDocument
{
    /// <summary>
    /// Gets the configured providers.
    /// </summary>
    public IReadOnlyList<ProviderConfig> Providers { get; init; } = [];

    /// <summary>
    /// Gets properties this version does not know about, kept so a save does not drop them.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; init; }
}
