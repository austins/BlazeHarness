using BlazeHarness.Core.Providers;

namespace BlazeHarness.Core.Tests.Unit.Providers;

public sealed class SamplingImportTests
{
    [Fact]
    public void Parse_BareSamplingObject_ReadsDefaults()
    {
        // Act
        var result = SamplingImport.Parse("""{"temperature": 0.6, "top_p": 0.95, "top_k": 20, "min_p": 0}""");

        // Assert
        result.Errors.Should().BeEmpty();
        result.Sampling.Should().BeEquivalentTo(new SamplingParams { Temperature = 0.6, TopP = 0.95, TopK = 20, MinP = 0 });
        result.Levels.Should().BeNull();
    }

    [Fact]
    public void Parse_CamelCaseNames_AreAccepted()
    {
        // Act
        var result = SamplingImport.Parse("""{"topP": 0.9, "presencePenalty": 1}""");

        // Assert
        result.Sampling!.TopP.Should().Be(0.9);
        result.Sampling.PresencePenalty.Should().Be(1);
    }

    [Fact]
    public void Parse_UnknownName_BecomesExtraField()
    {
        // Act
        var result = SamplingImport.Parse("""{"temperature": 1, "mirostat": 2}""");

        // Assert
        result.Sampling!.Extra.Should().ContainKey("mirostat").WhoseValue.GetInt32().Should().Be(2);
    }

    [Fact]
    public void Parse_FullShape_ReadsLevelsAndOverrides()
    {
        // Arrange
        const string json = """
            {
              "samplingParams": { "temperature": 1.0 },
              "samplingParamsByThinkingLevel": { "off": { "temperature": 0.7, "top_p": 0.8 } },
              "thinkingLevels": { "off": "none", "minimal": null, "high": "high" }
            }
            """;

        // Act
        var result = SamplingImport.Parse(json);

        // Assert
        result.Errors.Should().BeEmpty();
        result.Sampling!.Temperature.Should().Be(1.0);
        result.Levels.Should().NotBeNull();
        result.Levels.Keys.Should().BeEquivalentTo([ThinkingLevel.Off, ThinkingLevel.High]);
        result.Levels[ThinkingLevel.Off].Value.Should().Be("none");
        result.Levels[ThinkingLevel.Off].Overrides!.TopP.Should().Be(0.8);
        result.Levels[ThinkingLevel.High].Overrides.Should().BeNull();
    }

    [Fact]
    public void Parse_BooleanLevelValue_KeepsLiteral()
    {
        // Act
        var result = SamplingImport.Parse("""{"thinkingLevels": {"off": false, "high": true}}""");

        // Assert
        result.Levels![ThinkingLevel.Off].Value.Should().Be("false");
        result.Levels[ThinkingLevel.High].Value.Should().Be("true");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("""{"temperature": "hot"}""")]
    [InlineData("""{"top_k": 1.5}""")]
    [InlineData("""{"thinkingLevels": {"extreme": "x"}}""")]
    [InlineData("""{"thinkingLevels": {"off": {"a": 1}}}""")]
    [InlineData("""{"samplingParams": {}, "other": 1}""")]
    public void Parse_Invalid_ReportsErrors(string json)
    {
        // Act
        var result = SamplingImport.Parse(json);

        // Assert
        result.Errors.Should().NotBeEmpty();
        result.Sampling.Should().BeNull();
        result.Levels.Should().BeNull();
    }

    [Fact]
    public void Parse_NumericLevelName_IsRejected()
    {
        // Act
        var result = SamplingImport.Parse("""{"thinkingLevels": {"1": "x"}}""");

        // Assert
        result.Errors.Should().ContainSingle().Which.Should().Contain("Unknown thinking level");
    }
}
