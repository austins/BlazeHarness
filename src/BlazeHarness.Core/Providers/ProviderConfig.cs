using BlazeHarness.Core.Permissions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazeHarness.Core.Providers;

/// <summary>
/// The wire protocol a provider speaks.
/// </summary>
public enum ApiType
{
    /// <summary>The OpenAI-compatible <c>/chat/completions</c> API.</summary>
    ChatCompletions,
}

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

/// <summary>
/// How a thinking level is put on the wire; servers differ.
/// </summary>
public enum ThinkingSendMode
{
    /// <summary><c>reasoning_effort</c>: "value".</summary>
    ReasoningEffort,

    /// <summary><c>reasoning</c>: { "effort": "value" }.</summary>
    ReasoningObject,

    /// <summary>A custom body field addressed by a dotted path, e.g. <c>chat_template_kwargs.enable_thinking</c>.</summary>
    BodyField,
}

/// <summary>
/// The wire format used to send a thinking level.
/// </summary>
public sealed record ThinkingSend
{
    /// <summary>
    /// Gets how the level is sent.
    /// </summary>
    public ThinkingSendMode Mode { get; init; } = ThinkingSendMode.ReasoningEffort;

    /// <summary>
    /// Gets the dotted body path; only used with <see cref="ThinkingSendMode.BodyField"/>.
    /// </summary>
    public string? Field { get; init; }
}

/// <summary>
/// Sampling settings for a request.
/// </summary>
/// <remarks>
/// All values are optional: an unset value is omitted from the request so the server default applies.
/// </remarks>
public sealed record SamplingParams
{
    /// <summary>
    /// Gets the sampling temperature.
    /// </summary>
    public double? Temperature { get; init; }

    /// <summary>
    /// Gets the nucleus sampling probability mass.
    /// </summary>
    public double? TopP { get; init; }

    /// <summary>
    /// Gets how many of the most likely tokens to sample from. Not part of the OpenAI API; sent as a body field.
    /// </summary>
    public int? TopK { get; init; }

    /// <summary>
    /// Gets the minimum token probability relative to the most likely token. Not part of the OpenAI API; sent as a body field.
    /// </summary>
    public double? MinP { get; init; }

    /// <summary>
    /// Gets the penalty for tokens that already appeared at all.
    /// </summary>
    public double? PresencePenalty { get; init; }

    /// <summary>
    /// Gets the penalty that grows with how often a token already appeared.
    /// </summary>
    public double? FrequencyPenalty { get; init; }

    /// <summary>
    /// Gets the multiplicative repetition penalty. Not part of the OpenAI API; sent as a body field.
    /// </summary>
    public double? RepetitionPenalty { get; init; }

    /// <summary>
    /// Gets server-specific extra body fields.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>
    /// Layer another set of values over this one.
    /// </summary>
    /// <param name="overrides">The values that win where set, or null to keep this instance unchanged.</param>
    /// <returns>This instance with every value set on <paramref name="overrides"/> replacing its own.</returns>
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

/// <summary>
/// One entry of a model's <c>levels</c> table.
/// </summary>
/// <remarks>
/// A level that is absent from the table is unsupported by the model.
/// </remarks>
public sealed record LevelConfig
{
    /// <summary>
    /// Gets the provider's value for this level (e.g. "none", "high", "true"); null sends nothing.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>
    /// Gets sampling values that apply only while this level is selected.
    /// </summary>
    public SamplingParams? Overrides { get; init; }
}

/// <summary>
/// The kinds of input a model accepts.
/// </summary>
[Flags]
public enum InputTypes
{
    /// <summary>No input.</summary>
    None = 0,

    /// <summary>Text.</summary>
    Text = 1,

    /// <summary>Images.</summary>
    Image = 2,
}

/// <summary>
/// A model offered by a provider, with its limits and defaults.
/// </summary>
public sealed record ModelConfig
{
    /// <summary>
    /// Gets the model identifier sent to the provider.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the name shown in the UI, or null to show <see cref="Id"/>.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Gets the total tokens the model accepts.
    /// </summary>
    public int ContextWindow { get; init; }

    /// <summary>
    /// Gets the cap on a single response.
    /// </summary>
    public int MaxOutputTokens { get; init; }

    /// <summary>
    /// Gets a value indicating whether the model produces reasoning output.
    /// </summary>
    public bool Reasoning { get; init; }

    /// <summary>
    /// Gets the kinds of input the model accepts.
    /// </summary>
    public InputTypes Input { get; init; } = InputTypes.Text;

    /// <summary>
    /// Gets the model's default sampling values.
    /// </summary>
    public SamplingParams? SamplingParams { get; init; }

    /// <summary>
    /// Gets the supported thinking levels and how each one is sent, or null when thinking is not configurable.
    /// </summary>
    public IReadOnlyDictionary<ThinkingLevel, LevelConfig>? Levels { get; init; }

    /// <summary>
    /// Gets the thinking wire format for this model, overriding the provider's.
    /// </summary>
    public ThinkingSend? ThinkingSend { get; init; }

    /// <summary>
    /// Gets the name to show in the UI.
    /// </summary>
    public string Name => string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName;
}

/// <summary>
/// An OpenAI-compatible endpoint and the models it serves.
/// </summary>
public sealed record ProviderConfig
{
    /// <summary>
    /// Gets the stable identifier, also used to name the provider's secret store entry.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the name shown in the UI.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the wire protocol the provider speaks.
    /// </summary>
    public ApiType ApiType { get; init; } = ApiType.ChatCompletions;

    /// <summary>
    /// Gets the base URL of the API, e.g. <c>https://api.deepseek.com</c>.
    /// </summary>
    public required string BaseUrl { get; init; }

    /// <summary>
    /// Gets extra HTTP headers sent with every request.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// Gets where the key comes from.
    /// </summary>
    /// <remarks>
    /// <c>${ENV_VAR}</c> reads an environment variable; null uses the secret store entry named after the provider id.
    /// It is never the key itself.
    /// </remarks>
    public string? ApiKeyRef { get; init; }

    /// <summary>
    /// Gets a value indicating whether to send <c>stream_options</c> to get token usage while streaming.
    /// </summary>
    /// <remarks>
    /// Some servers reject <c>stream_options</c>, so sending it is optional.
    /// </remarks>
    public bool SendStreamUsage { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether to send <c>max_tokens</c> instead of <c>max_completion_tokens</c>.
    /// </summary>
    /// <remarks>
    /// Most compatible servers only honor <c>max_tokens</c>.
    /// </remarks>
    public bool LegacyMaxTokens { get; init; } = true;

    /// <summary>
    /// Gets the provider's default thinking wire format.
    /// </summary>
    public ThinkingSend ThinkingSend { get; init; } = new();

    /// <summary>
    /// Gets the models the provider offers.
    /// </summary>
    public IReadOnlyList<ModelConfig> Models { get; init; } = [];
}

/// <summary>
/// The contents of <c>config.json</c>.
/// </summary>
public sealed record AppConfig
{
    /// <summary>
    /// Gets the configured providers.
    /// </summary>
    public IReadOnlyList<ProviderConfig> Providers { get; init; } = [];

    /// <summary>
    /// Gets the provider preselected for new chats.
    /// </summary>
    public string? DefaultProviderId { get; init; }

    /// <summary>
    /// Gets the model preselected for new chats.
    /// </summary>
    public string? DefaultModelId { get; init; }

    /// <summary>
    /// Gets an optional cheaper model for titles and compaction, as <c>providerId/modelId</c>.
    /// </summary>
    public string? SideTaskModel { get; init; }

    /// <summary>
    /// Gets the access level new chats start with.
    /// </summary>
    /// <remarks>
    /// Only <see cref="AccessLevel.ReadOnly"/> and <see cref="AccessLevel.WriteInFolder"/> count; anything else is treated as the built-in default.
    /// </remarks>
    public AccessLevel DefaultAccess { get; init; } = AccessLevel.WriteInFolder;

    /// <summary>
    /// Gets the approval mode new chats start with.
    /// </summary>
    /// <remarks>
    /// Only <see cref="ApprovalMode.AskFirst"/> and <see cref="ApprovalMode.AutoApprove"/> count; anything else is treated as the built-in default.
    /// </remarks>
    public ApprovalMode DefaultApprovals { get; init; } = ApprovalMode.AskFirst;

    /// <summary>
    /// Gets the rules the user wrote to allow, ask about or refuse tool calls.
    /// </summary>
    public IReadOnlyList<PermissionRule> PermissionRules { get; init; } = [];

    /// <summary>
    /// Gets the folders no tool may touch, or null to use the built-in list.
    /// </summary>
    public IReadOnlyList<string>? ProtectedFolders { get; init; }

    /// <summary>
    /// Gets a value indicating whether the Unrestricted and Never-ask options are hidden.
    /// </summary>
    public bool DisableBypassModes { get; init; }

    /// <summary>
    /// Gets properties this version does not know about, kept so a save does not drop them.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; init; }
}
