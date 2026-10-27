using BlazeHarness.Core.Providers;
using Microsoft.Extensions.AI;
using System.Net;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Integration.Providers;

public sealed class ChatClientFactoryTests
{
    private const string Chunk1 =
        """{"id":"1","object":"chat.completion.chunk","model":"m","choices":[{"index":0,"delta":{"role":"assistant","reasoning_content":"thinking..."},"finish_reason":null}]}""";

    private const string Chunk2 =
        """{"id":"1","object":"chat.completion.chunk","model":"m","choices":[{"index":0,"delta":{"content":"Hello"},"finish_reason":null}]}""";

    private const string Chunk3 =
        """{"id":"1","object":"chat.completion.chunk","model":"m","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"read_file","arguments":"{\"path\":\"a\"}"}}]},"finish_reason":null}]}""";

    private const string Chunk4 =
        """{"id":"1","object":"chat.completion.chunk","model":"m","choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}""";

    private const string UsageChunk =
        """{"id":"1","object":"chat.completion.chunk","model":"m","choices":[],"usage":{"prompt_tokens":120,"completion_tokens":7,"total_tokens":127,"prompt_tokens_details":{"cached_tokens":100}}}""";

    [Fact]
    public async Task Streaming_ReasoningToolCallsAndUsage_AreAllReachable()
    {
        // Arrange
        using var server = new FakeOpenAiServer([Chunk1, Chunk2, Chunk3, Chunk4, UsageChunk]);
        var (provider, model) = CreateProvider(server.BaseUrl);
        using var http = new HttpClient();
        using var client = ChatClientFactory.Create(provider, model.Id, "key", http);
        var options = ChatClientFactory.BuildOptions(provider, model, ThinkingLevel.High);

        // Act
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "hi")],
                           options,
                           TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        // Assert
        updates.SelectMany(u => u.Contents).OfType<TextContent>().Select(c => c.Text).Should().Contain("Hello");
        updates.SelectMany(u => u.Contents).OfType<FunctionCallContent>().Should().ContainSingle(c => c.Name == "read_file");

        var usage = updates.SelectMany(u => u.Contents).OfType<UsageContent>().Single().Details;
        usage.InputTokenCount.Should().Be(120);
        usage.CachedInputTokenCount.Should().Be(100);

        var reasoning = updates.SelectMany(u => u.Contents).OfType<TextReasoningContent>().Select(c => c.Text);
        reasoning.Should().Contain("thinking...");
    }

    [Fact]
    public async Task Request_SamplingAndThinking_AreSerializedAsConfigured()
    {
        // Arrange
        using var server = new FakeOpenAiServer([Chunk2, UsageChunk]);
        var (provider, model) = CreateProvider(server.BaseUrl);
        using var http = new HttpClient();
        using var client = ChatClientFactory.Create(provider, model.Id, "key", http);
        var options = ChatClientFactory.BuildOptions(provider, model, ThinkingLevel.Off);

        // Act
        var count = 0;
        await foreach (var unused in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "hi")],
                           options,
                           TestContext.Current.CancellationToken))
        {
            count++;
        }

        count.Should().BeGreaterThan(0);

        // Assert
        TestContext.Current.TestOutputHelper?.WriteLine(server.RequestBodies.Single());
        using var body = JsonDocument.Parse(server.RequestBodies.Single());
        var root = body.RootElement;
        root.GetProperty("temperature").GetDouble().Should().Be(0.7);
        root.GetProperty("top_k").GetInt32().Should().Be(20);
        root.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean().Should().BeFalse();
        root.GetProperty("max_tokens").GetInt32().Should().Be(1024);
        root.GetProperty("stream_options").GetProperty("include_usage").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Request_StreamUsageDisabled_OmitsStreamOptions()
    {
        // Arrange
        using var server = new FakeOpenAiServer([Chunk2]);
        var (provider, model) = CreateProvider(server.BaseUrl);
        provider = provider with { SendStreamUsage = false };
        using var http = new HttpClient();
        using var client = ChatClientFactory.Create(provider, model.Id, null, http);

        // Act
        await client.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "hi")],
                ChatClientFactory.BuildOptions(provider, model, null),
                TestContext.Current.CancellationToken)
            .ToChatResponseAsync(TestContext.Current.CancellationToken);

        // Assert
        server.RequestBodies.Single().Should().NotContain("stream_options");
    }

    [Fact]
    public async Task Request_TransientFailure_IsRetried()
    {
        // Arrange
        using var server = new FakeOpenAiServer(
            _ => [Chunk2, UsageChunk],
            n => n == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        var (provider, model) = CreateProvider(server.BaseUrl);
        using var http = new HttpClient();
        using var client = ChatClientFactory.Create(provider, model.Id, "key", http, new NoDelayRetryPolicy(1));

        // Act
        var response = await client.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "hi")],
                ChatClientFactory.BuildOptions(provider, model, null),
                TestContext.Current.CancellationToken)
            .ToChatResponseAsync(TestContext.Current.CancellationToken);

        // Assert
        server.RequestCount.Should().Be(2);
        response.Text.Should().Contain("Hello");
    }

    private static (ProviderConfig Provider, ModelConfig Model) CreateProvider(Uri baseUrl)
    {
        var model = new ModelConfig
        {
            Id = "m",
            ContextWindow = 8192,
            MaxOutputTokens = 1024,
            Reasoning = true,
            SamplingParams = new SamplingParams { Temperature = 0.7, TopK = 20 },
            Levels = new Dictionary<ThinkingLevel, LevelConfig>
            {
                [ThinkingLevel.Off] = new() { Value = "false" },
                [ThinkingLevel.High] = new() { Value = "true" },
            },
        };

        var provider = new ProviderConfig
        {
            Id = "fake",
            DisplayName = "Fake",
            BaseUrl = baseUrl,
            ThinkingSend = new ThinkingSend { Mode = ThinkingSendMode.BodyField, Field = "chat_template_kwargs.enable_thinking" },
            Models = [model],
        };

        return (provider, model);
    }
}
