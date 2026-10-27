using BlazeHarness.Core.Providers;

namespace BlazeHarness.Core.Tests.Unit.Providers;

public sealed class ThinkingPresetTests
{
    [Fact]
    public void All_PresetIds_AreUnique()
    {
        // Act
        var ids = ThinkingPreset.All.Select(p => p.Id);

        // Assert
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void All_Presets_PassValidation()
    {
        foreach (var preset in ThinkingPreset.All)
        {
            // Arrange
            var provider = new ProviderConfig
            {
                Id = "p",
                DisplayName = "P",
                BaseUrl = new Uri("http://localhost/v1"),
                Models =
                [
                    new ModelConfig
                    {
                        Id = "m",
                        ContextWindow = 1000,
                        MaxOutputTokens = 100,
                        Reasoning = true,
                        SamplingParams = preset.Sampling,
                        Levels = preset.Levels,
                        ThinkingSend = preset.ThinkingSend,
                    },
                ],
            };

            // Act
            var issues = ProviderValidator.Validate(provider);

            // Assert
            issues.Should().BeEmpty(preset.Id);
        }
    }

    [Fact]
    public void Qwen3_OffLevel_SendsFalseAndUsesNonThinkingSampling()
    {
        // Arrange
        var preset = ThinkingPreset.Find("qwen3")!;
        var provider = new ProviderConfig { Id = "p", DisplayName = "P", BaseUrl = new Uri("http://localhost/v1") };
        var model = new ModelConfig
        {
            Id = "qwen3-32b",
            SamplingParams = preset.Sampling,
            Levels = preset.Levels,
            ThinkingSend = preset.ThinkingSend,
        };

        // Act
        var json = ResolvedRequestParams.Resolve(provider, model, ThinkingLevel.Off).ToRequestJson();

        // Assert
        json["temperature"]!.GetValue<double>().Should().Be(0.7);
        json["top_p"]!.GetValue<double>().Should().Be(0.8);
        json["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void Find_UnknownId_ReturnsNull()
    {
        // Act
        var result = ThinkingPreset.Find("nope");

        // Assert
        result.Should().BeNull();
    }
}
