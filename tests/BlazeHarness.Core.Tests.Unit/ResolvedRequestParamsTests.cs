using BlazeHarness.Core.Providers;

namespace BlazeHarness.Core.Tests.Unit;

public sealed class ResolvedRequestParamsTests
{
    private static readonly ProviderConfig Provider = new()
    {
        Id = "p",
        DisplayName = "P",
        BaseUrl = "http://localhost/v1",
    };

    [Fact]
    public void Resolve_LevelOverrides_WinOverModelDefaults()
    {
        // Arrange
        var model = new ModelConfig
        {
            Id = "m",
            SamplingParams = new SamplingParams { Temperature = 1.0, TopP = 0.95, PresencePenalty = 1.5 },
            Levels = new Dictionary<ThinkingLevel, LevelConfig>
            {
                [ThinkingLevel.Off] = new() { Value = "none", Overrides = new SamplingParams { Temperature = 0.7, TopP = 0.8 } },
            },
        };

        // Act
        var result = ResolvedRequestParams.Resolve(Provider, model, ThinkingLevel.Off, null);

        // Assert
        result.Sampling.Temperature.Should().Be(0.7);
        result.Sampling.TopP.Should().Be(0.8);
        result.Sampling.PresencePenalty.Should().Be(1.5);
    }

    [Fact]
    public void Resolve_SessionOverride_WinsOverLevel()
    {
        // Arrange
        var model = new ModelConfig
        {
            Id = "m",
            SamplingParams = new SamplingParams { Temperature = 1.0 },
            Levels = new Dictionary<ThinkingLevel, LevelConfig>
            {
                [ThinkingLevel.High] = new() { Overrides = new SamplingParams { Temperature = 0.6 } },
            },
        };

        // Act
        var result = ResolvedRequestParams.Resolve(Provider, model, ThinkingLevel.High, new SamplingParams { Temperature = 0.2 });

        // Assert
        result.Sampling.Temperature.Should().Be(0.2);
    }

    [Fact]
    public void Resolve_NothingSet_OmitsEverything()
    {
        // Act
        var result = ResolvedRequestParams.Resolve(Provider, new ModelConfig { Id = "m" }, null, null);

        // Assert
        result.Sampling.Temperature.Should().BeNull();
        result.BodyFields.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_NonStandardSampling_GoesToBodyFields()
    {
        // Arrange
        var model = new ModelConfig
        {
            Id = "m",
            SamplingParams = new SamplingParams { TopK = 20, MinP = 0.05, RepetitionPenalty = 1.1, Temperature = 0.5 },
        };

        // Act
        var result = ResolvedRequestParams.Resolve(Provider, model, null, null);

        // Assert
        result.BodyFields.ToJsonString().Should().Be("""{"top_k":20,"min_p":0.05,"repetition_penalty":1.1}""");
    }

    [Theory]
    [InlineData(ThinkingSendMode.ReasoningEffort, null, """{"reasoning_effort":"high"}""")]
    [InlineData(ThinkingSendMode.ReasoningObject, null, """{"reasoning":{"effort":"high"}}""")]
    [InlineData(ThinkingSendMode.BodyField, "chat_template_kwargs.effort", """{"chat_template_kwargs":{"effort":"high"}}""")]
    public void Resolve_ThinkingLevel_UsesConfiguredWireFormat(ThinkingSendMode mode, string? field, string expected)
    {
        // Arrange
        var provider = Provider with { ThinkingSend = new ThinkingSend { Mode = mode, Field = field } };
        var model = new ModelConfig
        {
            Id = "m",
            Levels = new Dictionary<ThinkingLevel, LevelConfig> { [ThinkingLevel.High] = new() { Value = "high" } },
        };

        // Act
        var result = ResolvedRequestParams.Resolve(provider, model, ThinkingLevel.High, null);

        // Assert
        result.BodyFields.ToJsonString().Should().Be(expected);
    }

    [Fact]
    public void Resolve_BodyFieldBooleanValue_IsSentAsBoolean()
    {
        // Arrange
        var provider = Provider with
        {
            ThinkingSend = new ThinkingSend { Mode = ThinkingSendMode.BodyField, Field = "chat_template_kwargs.enable_thinking" },
        };
        var model = new ModelConfig
        {
            Id = "m",
            Levels = new Dictionary<ThinkingLevel, LevelConfig> { [ThinkingLevel.Off] = new() { Value = "false" } },
        };

        // Act
        var result = ResolvedRequestParams.Resolve(provider, model, ThinkingLevel.Off, null);

        // Assert
        result.BodyFields.ToJsonString().Should().Be("""{"chat_template_kwargs":{"enable_thinking":false}}""");
    }

    [Fact]
    public void Resolve_UnsupportedLevel_SendsNothing()
    {
        // Arrange
        var model = new ModelConfig
        {
            Id = "m",
            Levels = new Dictionary<ThinkingLevel, LevelConfig> { [ThinkingLevel.High] = new() { Value = "high" } },
        };

        // Act
        var result = ResolvedRequestParams.Resolve(Provider, model, ThinkingLevel.Low, null);

        // Assert
        result.BodyFields.Should().BeEmpty();
    }
}
