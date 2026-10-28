using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Providers;
using BlazeHarness.Core.Sessions;
using BlazeHarness.Tests.Shared;
using BlazeHarness.Tests.Shared.Agent;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Unit.Sessions;

public sealed class ModelTitleGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_FirstExchange_AsksForAShortCappedTitle()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("Weather "), ScriptedChatClient.Text("forecast")]);
        var sut = Generator(client);

        // Act
        var title = await sut.GenerateAsync(Target(), "What is the weather?", "Sunny.", ct);

        // Assert
        title.Should().Be("Weather forecast");
        client.Requests.Should().ContainSingle();
        var request = client.Requests[0];
        request[0].Role.Should().Be(ChatRole.System);
        request[1].Text.Should().Be("User: What is the weather?\n\nAssistant: Sunny.");
        client.Options[0]!.MaxOutputTokens.Should().Be(256);
    }

    [Fact]
    public async Task GenerateAsync_StoppedPartWay_ThrowsCancellationInsteadOfReturningHalfATitle()
    {
        // Arrange
        using var stop = new CancellationTokenSource();
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("Weath")], cancelAfterwards: stop);
        var sut = Generator(client);

        // Act
        var act = async () => await sut.GenerateAsync(Target(), "What is the weather?", "Sunny.", stop.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>().WaitAsync(TestTimeouts.Default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GenerateAsync_LongExchange_SendsOnlyAnExcerpt()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("Long")]);
        var sut = Generator(client);

        // Act
        await sut.GenerateAsync(Target(), new string('a', 5000), new string('b', 5000), ct);

        // Assert
        client.Requests[0][1].Text.Should().HaveLength("User: ".Length + 601 + "\n\nAssistant: ".Length + 601);
    }

    [Fact]
    public async Task GenerateAsync_ModelThatCanThinkOff_TurnsThinkingOff()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("Title")]);
        var sut = Generator(client);
        var model = new ModelConfig
        {
            Id = "m",
            ContextWindow = 8000,
            MaxOutputTokens = 1000,
            Levels = new Dictionary<ThinkingLevel, LevelConfig>
            {
                [ThinkingLevel.Off] = new LevelConfig { Overrides = new SamplingParams { Temperature = 0.1 } },
                [ThinkingLevel.High] = new LevelConfig { Overrides = new SamplingParams { Temperature = 0.9 } },
            },
        };

        // Act
        await sut.GenerateAsync(Target(model, ThinkingLevel.High), "hi", "hello", ct);

        // Assert
        client.Options[0]!.Temperature.Should().BeApproximately(0.1f, 0.001f);
    }

    [Fact]
    public async Task GenerateAsync_ServerError_Throws()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient([], failure: new InvalidOperationException("down"));
        var sut = Generator(client);

        // Act
#pragma warning disable IDISP013 // The client is disposed after the awaited assertion.
        var act = () => sut.GenerateAsync(Target(), "hi", "hello", ct);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("down");
#pragma warning restore IDISP013
    }

    [Fact]
    public async Task GenerateAsync_EmptyAnswer_ReturnsNull()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient([]);
        var sut = Generator(client);

        // Act
        var title = await sut.GenerateAsync(Target(), "hi", "hello", ct);

        // Assert
        title.Should().BeNull();
    }

    [Theory]
    [InlineData("\"Weather forecast\"", "Weather forecast")]
    [InlineData("Title: Weather forecast.", "Weather forecast")]
    [InlineData("**Weather   forecast**", "Weather forecast")]
    [InlineData("“Weather forecast”\n\nThis title covers the forecast.", "Weather forecast")]
    [InlineData("\n\n  Weather forecast  \n", "Weather forecast")]
    [InlineData("\"\"", null)]
    [InlineData("   ", null)]
    public void Clean_ModelAnswer_ReturnsATidyTitle(string raw, string? expected)
    {
        // Arrange / Act
        var title = ModelTitleGenerator.Clean(raw);

        // Assert
        title.Should().Be(expected);
    }

    [Fact]
    public void Clean_VeryLongAnswer_IsCutShort()
    {
        // Arrange
        var raw = new string('a', 200);

        // Act
        var title = ModelTitleGenerator.Clean(raw);

        // Assert
        title.Should().HaveLength(ModelTitleGenerator.MaxTitleLength + 1).And.EndWith("…");
    }

    private static ModelTitleGenerator Generator(IChatClient client)
    {
        return new ModelTitleGenerator((_, _) => Task.FromResult(client));
    }

    private static ChatTarget Target(ModelConfig? model = null, ThinkingLevel? level = null)
    {
        model ??= new ModelConfig { Id = "m", ContextWindow = 8000, MaxOutputTokens = 1000 };
        var provider = new ProviderConfig { Id = "p", DisplayName = "P", BaseUrl = new Uri("http://localhost/v1"), LegacyMaxTokens = false, Models = [model] };
        return new ChatTarget(provider, model, level);
    }
}
