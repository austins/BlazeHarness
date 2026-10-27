namespace BlazeHarness.Core.Providers;

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
    public required Uri BaseUrl { get; init; }

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
