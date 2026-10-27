using BlazeHarness.Core.Providers;
using BlazeHarness.Core.Secrets;
using System.Collections.ObjectModel;

namespace BlazeHarness.Web.Settings;

/// <summary>
/// A mutable copy of a <see cref="ProviderConfig"/> for the editing form.
/// </summary>
public sealed class ProviderEditModel
{
    /// <summary>
    /// Gets or sets a value indicating whether the provider is not saved yet.
    /// </summary>
    public bool IsNew { get; set; }

    /// <summary>
    /// Gets or sets the provider id. It is fixed once saved because it names the secret entry.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name shown in the UI.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the API base URL.
    /// </summary>
#pragma warning disable CA1056 // Bound directly to a text input in the form before validation.
    public string BaseUrl { get; set; } = string.Empty;
#pragma warning restore CA1056

    /// <summary>
    /// Gets or sets the environment variable that holds the key; empty uses the stored key.
    /// </summary>
    public string ApiKeyEnvVar { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a newly typed API key. Empty leaves the stored key unchanged.
    /// </summary>
    public string NewApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether to request token usage while streaming.
    /// </summary>
    public bool SendStreamUsage { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to send <c>max_tokens</c> rather than <c>max_completion_tokens</c>.
    /// </summary>
    public bool LegacyMaxTokens { get; set; } = true;

    /// <summary>
    /// Gets or sets how thinking levels are sent by default.
    /// </summary>
    public ThinkingSendMode ThinkingMode { get; set; } = ThinkingSendMode.ReasoningEffort;

    /// <summary>
    /// Gets or sets the body field used with <see cref="ThinkingSendMode.BodyField"/>.
    /// </summary>
    public string ThinkingField { get; set; } = string.Empty;

    /// <summary>
    /// Gets the extra headers.
    /// </summary>
    public Collection<HeaderRow> Headers { get; } = [];

    /// <summary>
    /// Gets the models.
    /// </summary>
    public Collection<ModelEditModel> Models { get; } = [];

    /// <summary>
    /// Create an editable copy of a provider.
    /// </summary>
    /// <param name="provider">The provider to copy.</param>
    /// <returns>The editing model.</returns>
    public static ProviderEditModel FromConfig(ProviderConfig provider)
    {
        var envVar = string.Empty;
        if (provider.ApiKeyRef is not null)
        {
            ApiKeyResolver.TryGetEnvironmentName(provider.ApiKeyRef, out envVar);
        }

        var model = new ProviderEditModel
        {
            Id = provider.Id,
            DisplayName = provider.DisplayName,
            BaseUrl = provider.BaseUrl.OriginalString,
            ApiKeyEnvVar = envVar,
            SendStreamUsage = provider.SendStreamUsage,
            LegacyMaxTokens = provider.LegacyMaxTokens,
            ThinkingMode = provider.ThinkingSend.Mode,
            ThinkingField = provider.ThinkingSend.Field ?? string.Empty,
        };

        foreach (var (name, value) in provider.Headers ?? new Dictionary<string, string>())
        {
            model.Headers.Add(new HeaderRow { Name = name, Value = value });
        }

        foreach (var configured in provider.Models)
        {
            model.Models.Add(ModelEditModel.FromConfig(configured));
        }

        return model;
    }

    /// <summary>
    /// Convert back to the stored form.
    /// </summary>
    /// <returns>The provider config.</returns>
    public ProviderConfig ToConfig()
    {
        var headers = Headers
            .Where(h => !string.IsNullOrWhiteSpace(h.Name))
            .GroupBy(h => h.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Value);

        return new ProviderConfig
        {
            Id = Id.Trim(),
            DisplayName = DisplayName.Trim(),
            BaseUrl = Uri.TryCreate(BaseUrl.Trim(), UriKind.RelativeOrAbsolute, out var uri) ? uri : new Uri(string.Empty, UriKind.Relative),
            ApiKeyRef = string.IsNullOrWhiteSpace(ApiKeyEnvVar) ? null : "${" + ApiKeyEnvVar.Trim() + "}",
            Headers = headers.Count == 0 ? null : headers,
            SendStreamUsage = SendStreamUsage,
            LegacyMaxTokens = LegacyMaxTokens,
            ThinkingSend = new ThinkingSend
            {
                Mode = ThinkingMode,
                Field = ThinkingMode == ThinkingSendMode.BodyField ? ThinkingField.Trim() : null,
            },
            Models = [.. Models.Select(m => m.ToConfig())],
        };
    }
}
