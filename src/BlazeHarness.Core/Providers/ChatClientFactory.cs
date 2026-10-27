using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;

namespace BlazeHarness.Core.Providers;

/// <summary>
/// Builds <see cref="IChatClient"/> instances and per-request <see cref="ChatOptions"/> for an OpenAI-compatible endpoint.
/// </summary>
public static class ChatClientFactory
{
    private const int MaxRetries = 4;

    // The SDK insists on a credential even when the server needs none.
    private const string PlaceholderApiKey = "not-needed";

    // Local models can take minutes to start answering a long prompt.
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Create a streaming chat client for a provider.
    /// </summary>
    /// <remarks>
    /// Transient failures (429, 5xx, network errors) are retried with backoff. The provider's extra headers are added to every request.
    /// </remarks>
    /// <param name="provider">The provider to talk to.</param>
    /// <param name="modelId">The model identifier to request.</param>
    /// <param name="apiKey">The API key, or null for keyless local servers.</param>
    /// <param name="httpClient">The HTTP client used for transport. The caller owns it and must keep it alive as long as the client.</param>
    /// <returns>A client that streams chat completions from the provider.</returns>
    public static IChatClient Create(ProviderConfig provider, string modelId, string? apiKey, HttpClient httpClient)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(provider.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute),
            Transport = new HttpClientPipelineTransport(httpClient),
            RetryPolicy = new ClientRetryPolicy(MaxRetries),
            NetworkTimeout = NetworkTimeout,
        };

        if (provider.Headers is { Count: > 0 })
        {
            options.AddPolicy(new HeadersPolicy(provider.Headers), PipelinePosition.PerCall);
        }

        var credential = new ApiKeyCredential(string.IsNullOrEmpty(apiKey) ? PlaceholderApiKey : apiKey);
        return new ChatClient(modelId, credential, options).AsIChatClient();
    }

    /// <summary>
    /// Build the options for one request.
    /// </summary>
    /// <remarks>
    /// Values every server understands go on <see cref="ChatOptions"/>; everything else (non-standard sampling fields,
    /// the thinking value, <c>max_tokens</c>, and removing <c>stream_options</c>) is patched into the raw request body.
    /// </remarks>
    /// <param name="provider">The provider, which decides the wire format.</param>
    /// <param name="model">The model, which supplies defaults and limits.</param>
    /// <param name="level">The selected thinking level, or null to send no thinking setting.</param>
    /// <returns>The options to pass to the chat client.</returns>
    public static ChatOptions BuildOptions(
        ProviderConfig provider,
        ModelConfig model,
        ThinkingLevel? level)
    {
        var resolved = ResolvedRequestParams.Resolve(provider, model, level);
        var sampling = resolved.Sampling;

        return new ChatOptions
        {
            ModelId = model.Id,
            Temperature = (float?)sampling.Temperature,
            TopP = (float?)sampling.TopP,
            PresencePenalty = (float?)sampling.PresencePenalty,
            FrequencyPenalty = (float?)sampling.FrequencyPenalty,
            MaxOutputTokens = model.MaxOutputTokens > 0 && !provider.LegacyMaxTokens ? model.MaxOutputTokens : null,
            RawRepresentationFactory = _ =>
            {
                var raw = new ChatCompletionOptions();
                if (!provider.SendStreamUsage)
                {
                    raw.Patch.Remove("$.stream_options"u8);
                }

                if (model.MaxOutputTokens > 0 && provider.LegacyMaxTokens)
                {
                    raw.Patch.Set("$.max_tokens"u8, model.MaxOutputTokens);
                }

                foreach (var (key, value) in resolved.BodyFields)
                {
                    raw.Patch.Set(Encoding.UTF8.GetBytes("$." + key), Encoding.UTF8.GetBytes(value?.ToJsonString() ?? "null"));
                }

                return raw;
            },
        };
    }

    private sealed class HeadersPolicy(IReadOnlyDictionary<string, string> headers) : PipelinePolicy
    {
        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            Apply(message);
            ProcessNext(message, pipeline, currentIndex);
        }

        public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            Apply(message);
            return ProcessNextAsync(message, pipeline, currentIndex);
        }

        private void Apply(PipelineMessage message)
        {
            foreach (var (name, value) in headers)
            {
                message.Request.Headers.Set(name, value);
            }
        }
    }
}
