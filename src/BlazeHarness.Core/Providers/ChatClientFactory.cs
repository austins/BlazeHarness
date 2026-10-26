using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;

namespace BlazeHarness.Core.Providers;

/// <summary>Builds <see cref="IChatClient"/> instances and per-request <see cref="ChatOptions"/> for an OpenAI-compatible endpoint.</summary>
public static class ChatClientFactory
{
    private const int MaxRetries = 4;

    /// <summary>Creates a client. <c>apiKey</c> may be null for keyless local servers; the caller owns <c>httpClient</c>.</summary>
    public static IChatClient Create(ProviderConfig provider, string modelId, string? apiKey, HttpClient httpClient)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(provider.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute),
            Transport = new HttpClientPipelineTransport(httpClient),
            RetryPolicy = new ClientRetryPolicy(MaxRetries),
        };

        if (provider.Headers is { Count: > 0 })
        {
            options.AddPolicy(new HeadersPolicy(provider.Headers), PipelinePosition.PerCall);
        }

        var credential = new ApiKeyCredential(string.IsNullOrEmpty(apiKey) ? "not-needed" : apiKey);
        return new ChatClient(modelId, credential, options).AsIChatClient();
    }

    public static ChatOptions BuildOptions(
        ProviderConfig provider,
        ModelConfig model,
        ThinkingLevel? level,
        SamplingParams? sessionOverride)
    {
        var resolved = ResolvedRequestParams.Resolve(provider, model, level, sessionOverride);
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
