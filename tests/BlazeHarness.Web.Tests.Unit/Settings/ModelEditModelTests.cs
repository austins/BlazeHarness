using BlazeHarness.Core.Providers;
using BlazeHarness.Web.Settings;

namespace BlazeHarness.Web.Tests.Unit.Settings;

public sealed class ModelEditModelTests
{
    [Fact]
    public void ToConfig_NoLevelsSupported_StoresNoLevelsAndNoSendOverride()
    {
        // Arrange
        var sut = new ModelEditModel { Id = "m" };

        // Act
        var result = sut.ToConfig();

        // Assert
        result.Levels.Should().BeNull();
        result.ThinkingSend.Should().BeNull();
        result.SamplingParams.Should().BeNull();
    }

    [Fact]
    public void Apply_Qwen3Preset_MarksThinkingAndSetsLevelsAndSend()
    {
        // Arrange
        var sut = new ModelEditModel { Id = "qwen3-32b" };

        // Act
        sut.Apply(ThinkingPreset.Find("qwen3")!);
        var result = sut.ToConfig();

        // Assert
        result.Reasoning.Should().BeTrue();
        result.SamplingParams!.TopK.Should().Be(20);
        result.Levels![ThinkingLevel.Off].Value.Should().Be("false");
        result.ThinkingSend!.Mode.Should().Be(ThinkingSendMode.BodyField);
        result.ThinkingSend.Field.Should().Be("chat_template_kwargs.enable_thinking");
    }

    [Fact]
    public void ToConfig_RoundTrip_PreservesLevelOverrides()
    {
        // Arrange
        var original = new ModelConfig
        {
            Id = "m",
            Reasoning = true,
            Levels = new Dictionary<ThinkingLevel, LevelConfig>
            {
                [ThinkingLevel.Off] = new LevelConfig { Value = "none", Overrides = new SamplingParams { Temperature = 0.7 } },
                [ThinkingLevel.High] = new LevelConfig { Value = "high" },
            },
        };

        // Act
        var sut = ModelEditModel.FromConfig(original);
        var result = sut.ToConfig();

        // Assert
        sut.Levels[ThinkingLevel.Off].Supported.Should().BeTrue();
        sut.Levels[ThinkingLevel.Low].Supported.Should().BeFalse();
        result.Levels![ThinkingLevel.Off].Overrides!.Temperature.Should().Be(0.7);
        result.Levels[ThinkingLevel.High].Value.Should().Be("high");
        result.Levels.Should().NotContainKey(ThinkingLevel.Low);
    }

    [Fact]
    public void Apply_ValidImport_ReplacesSamplingAndLevels()
    {
        // Arrange
        var sut = new ModelEditModel { Id = "m" };
        var import = SamplingImport.Parse("""{"samplingParams":{"temperature":0.3,"top_k":10}}""");

        // Act
        sut.Apply(import);

        // Assert
        import.Errors.Should().BeEmpty();
        sut.Sampling.Get("temperature").Should().Be(0.3);
        sut.Sampling.Get("topK").Should().Be(10);
    }
}
