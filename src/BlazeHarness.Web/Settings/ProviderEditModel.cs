using BlazeHarness.Core.Providers;
using BlazeHarness.Core.Secrets;
using System.Collections.ObjectModel;

namespace BlazeHarness.Web.Settings;

/// <summary>
/// A mutable copy of a <see cref="ModelConfig"/> for the editing form.
/// </summary>
public sealed class ModelEditModel
{
    /// <summary>
    /// Gets or sets the model id sent to the provider.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name; empty shows the id.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the context window in tokens.
    /// </summary>
    public int ContextWindow { get; set; }

    /// <summary>
    /// Gets or sets the cap on a single response in tokens.
    /// </summary>
    public int MaxOutputTokens { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the model produces reasoning output.
    /// </summary>
    public bool Reasoning { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the model accepts images.
    /// </summary>
    public bool Image { get; set; }

    /// <summary>
    /// Gets or sets the model's default sampling values.
    /// </summary>
    public SamplingParams? SamplingParams { get; set; }

    /// <summary>
    /// Gets or sets the supported thinking levels, or null when thinking is not configurable.
    /// </summary>
    public IReadOnlyDictionary<ThinkingLevel, LevelConfig>? Levels { get; set; }

    /// <summary>
    /// Gets or sets the model's own thinking wire format, overriding the provider's.
    /// </summary>
    public ThinkingSend? ThinkingSend { get; set; }

    /// <summary>
    /// Create an editable copy of a model.
    /// </summary>
    /// <param name="model">The model to copy.</param>
    /// <returns>The editing model.</returns>
    public static ModelEditModel FromConfig(ModelConfig model)
    {
        return new ModelEditModel
        {
            Id = model.Id,
            DisplayName = model.DisplayName ?? string.Empty,
            ContextWindow = model.ContextWindow,
            MaxOutputTokens = model.MaxOutputTokens,
            Reasoning = model.Reasoning,
            Image = model.Input.HasFlag(InputTypes.Image),
            SamplingParams = model.SamplingParams,
            Levels = model.Levels,
            ThinkingSend = model.ThinkingSend,
        };
    }

    /// <summary>
    /// Create a model with the limits of a well-known model prefilled.
    /// </summary>
    /// <param name="id">The model id.</param>
    /// <returns>The new editing model; limits are left at zero when the model is not known.</returns>
    public static ModelEditModel FromId(string id)
    {
        var model = new ModelEditModel { Id = id };
        if (ModelPreset.Find(id) is { } preset)
        {
            model.ContextWindow = preset.ContextWindow;
            model.MaxOutputTokens = preset.MaxOutputTokens;
            model.Reasoning = preset.Reasoning;
            model.Image = preset.Image;
        }

        return model;
    }

    /// <summary>
    /// Convert back to the stored form.
    /// </summary>
    /// <returns>The model config.</returns>
    public ModelConfig ToConfig()
    {
        return new ModelConfig
        {
            Id = Id.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName.Trim(),
            ContextWindow = ContextWindow,
            MaxOutputTokens = MaxOutputTokens,
            Reasoning = Reasoning,
            Input = Image ? InputTypes.Text | InputTypes.Image : InputTypes.Text,
            SamplingParams = SamplingParams,
            Levels = Levels,
            ThinkingSend = ThinkingSend,
        };
    }
}

/// <summary>
/// One extra HTTP header row in the editing form.
/// </summary>
public sealed class HeaderRow
{
    /// <summary>
    /// Gets or sets the header name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the header value.
    /// </summary>
    public string Value { get; set; } = string.Empty;
}

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
    public string BaseUrl { get; set; } = string.Empty;

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
            BaseUrl = provider.BaseUrl,
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
            BaseUrl = BaseUrl.Trim(),
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
