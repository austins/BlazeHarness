using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazeHarness.Core.Providers;

public enum ApiType
{
    ChatCompletions,
}

public enum ThinkingLevel
{
    Off,
    Minimal,
    Low,
    Medium,
    High,
    Xhigh,
}

/// <summary>How a thinking level is put on the wire; servers differ.</summary>
public enum ThinkingSendMode
{
    /// <summary><c>reasoning_effort</c>: "value".</summary>
    ReasoningEffort,

    /// <summary><c>reasoning</c>: { "effort": "value" }.</summary>
    ReasoningObject,

    /// <summary>A custom body field addressed by a dotted path, e.g. <c>chat_template_kwargs.enable_thinking</c>.</summary>
    BodyField,
}

public sealed record ThinkingSend
{
    public ThinkingSendMode Mode { get; init; } = ThinkingSendMode.ReasoningEffort;

    /// <summary>Dotted body path; only used with <see cref="ThinkingSendMode.BodyField"/>.</summary>
    public string? Field { get; init; }
}

/// <summary>All values optional: an unset value is omitted from the request so the server default applies.</summary>
public sealed record SamplingParams
{
    public double? Temperature { get; init; }

    public double? TopP { get; init; }

    public int? TopK { get; init; }

    public double? MinP { get; init; }

    public double? PresencePenalty { get; init; }

    public double? FrequencyPenalty { get; init; }

    public double? RepetitionPenalty { get; init; }

    /// <summary>Server-specific extra body fields.</summary>
    public IReadOnlyDictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>Returns this instance with every value set on <paramref name="overrides"/> replacing its own.</summary>
    public SamplingParams MergeOver(SamplingParams? overrides)
    {
        if (overrides is null)
        {
            return this;
        }

        Dictionary<string, JsonElement>? extra = null;
        if (Extra is not null || overrides.Extra is not null)
        {
            extra = Extra is null ? [] : new Dictionary<string, JsonElement>(Extra);
            if (overrides.Extra is not null)
            {
                foreach (var (key, value) in overrides.Extra)
                {
                    extra[key] = value;
                }
            }
        }

        return new SamplingParams
        {
            Temperature = overrides.Temperature ?? Temperature,
            TopP = overrides.TopP ?? TopP,
            TopK = overrides.TopK ?? TopK,
            MinP = overrides.MinP ?? MinP,
            PresencePenalty = overrides.PresencePenalty ?? PresencePenalty,
            FrequencyPenalty = overrides.FrequencyPenalty ?? FrequencyPenalty,
            RepetitionPenalty = overrides.RepetitionPenalty ?? RepetitionPenalty,
            Extra = extra,
        };
    }
}

/// <summary>One entry of a model's <c>levels</c> table. A level that is absent is unsupported by the model.</summary>
public sealed record LevelConfig
{
    /// <summary>The provider's value for this level (e.g. "none", "high", "true"); null sends nothing.</summary>
    public string? Value { get; init; }

    public SamplingParams? Overrides { get; init; }
}

[Flags]
public enum InputTypes
{
    None = 0,
    Text = 1,
    Image = 2,
}

public sealed record ModelConfig
{
    public required string Id { get; init; }

    public string? DisplayName { get; init; }

    /// <summary>Total tokens the model accepts.</summary>
    public int ContextWindow { get; init; }

    /// <summary>Cap on a single response.</summary>
    public int MaxOutputTokens { get; init; }

    public bool Reasoning { get; init; }

    public InputTypes Input { get; init; } = InputTypes.Text;

    public SamplingParams? SamplingParams { get; init; }

    public IReadOnlyDictionary<ThinkingLevel, LevelConfig>? Levels { get; init; }

    /// <summary>Overrides the provider's thinking wire format for this model.</summary>
    public ThinkingSend? ThinkingSend { get; init; }

    public string Name => string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName;
}

public sealed record ProviderConfig
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public ApiType ApiType { get; init; } = ApiType.ChatCompletions;

    public required string BaseUrl { get; init; }

    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// Where the key comes from: <c>${ENV_VAR}</c> for an environment variable, or null to use the
    /// secret store entry named after the provider id. Never the key itself.
    /// </summary>
    public string? ApiKeyRef { get; init; }

    /// <summary>Some servers reject <c>stream_options</c>, so sending it is optional.</summary>
    public bool SendStreamUsage { get; init; } = true;

    /// <summary>Send <c>max_tokens</c> instead of <c>max_completion_tokens</c>; most compatible servers only honor the former.</summary>
    public bool LegacyMaxTokens { get; init; } = true;

    public ThinkingSend ThinkingSend { get; init; } = new();

    public IReadOnlyList<ModelConfig> Models { get; init; } = [];
}

public sealed record AppConfig
{
    public IReadOnlyList<ProviderConfig> Providers { get; init; } = [];

    public string? DefaultProviderId { get; init; }

    public string? DefaultModelId { get; init; }

    /// <summary>Optional cheaper model for titles and compaction, as <c>providerId/modelId</c>.</summary>
    public string? SideTaskModel { get; init; }

    /// <summary>When true, the Unrestricted and Never-ask options are not offered.</summary>
    public bool DisableBypassModes { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; init; }
}
