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
    public void ToConfig_RoundTrip_SharesThinkingSamplingAcrossNonOffLevels()
    {
        // Arrange
        var original = new ModelConfig
        {
            Id = "m",
            Reasoning = true,
            Levels = new Dictionary<ThinkingLevel, LevelConfig>
            {
                [ThinkingLevel.Off] = new LevelConfig { Value = "none" },
                [ThinkingLevel.Low] = new LevelConfig { Value = "low", Overrides = new SamplingParams { Temperature = 0.7 } },
                [ThinkingLevel.High] = new LevelConfig { Value = "high", Overrides = new SamplingParams { Temperature = 0.7 } },
            },
        };

        // Act
        var sut = ModelEditModel.FromConfig(original);
        var result = sut.ToConfig();

        // Assert
        sut.Levels[ThinkingLevel.Off].Supported.Should().BeTrue();
        sut.Levels[ThinkingLevel.Minimal].Supported.Should().BeFalse();
        result.Levels![ThinkingLevel.Off].Overrides.Should().BeNull();
        result.Levels[ThinkingLevel.Low].Overrides!.Temperature.Should().Be(0.7);
        result.Levels[ThinkingLevel.High].Overrides!.Temperature.Should().Be(0.7);
        result.Levels[ThinkingLevel.High].Value.Should().Be("high");
        result.Levels.Should().NotContainKey(ThinkingLevel.Minimal);
    }

    [Theory]
    [InlineData(ThinkingLevel.Off, "none")]
    [InlineData(ThinkingLevel.Xhigh, "xhigh")]
    public void SetLevelSupported_EmptyValue_FillsSuggestedValue(ThinkingLevel level, string expected)
    {
        // Arrange
        var sut = new ModelEditModel { Id = "m" };

        // Act
        sut.SetLevelSupported(level, true);

        // Assert
        sut.Levels[level].Supported.Should().BeTrue();
        sut.Levels[level].Value.Should().Be(expected);
    }

    [Fact]
    public void SetLevelSupported_ExistingValue_KeepsIt()
    {
        // Arrange
        var sut = new ModelEditModel { Id = "m" };
        sut.Levels[ThinkingLevel.Low].Value = "true";

        // Act
        sut.SetLevelSupported(ThinkingLevel.Low, true);

        // Assert
        sut.Levels[ThinkingLevel.Low].Value.Should().Be("true");
    }
}
