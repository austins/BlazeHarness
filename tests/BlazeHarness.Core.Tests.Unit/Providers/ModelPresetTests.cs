using BlazeHarness.Core.Providers;

namespace BlazeHarness.Core.Tests.Unit.Providers;

public sealed class ModelPresetTests
{
    [Theory]
    [InlineData("gpt-4o", 128_000)]
    [InlineData("GPT-4O-mini", 128_000)]
    [InlineData("openai/gpt-4o-2024-08-06", 128_000)]
    [InlineData("deepseek-reasoner", 128_000)]
    public void Find_KnownModel_ReturnsPreset(string modelId, int contextWindow)
    {
        // Act
        var preset = ModelPreset.Find(modelId);

        // Assert
        preset.Should().NotBeNull();
        preset.ContextWindow.Should().Be(contextWindow);
    }

    [Fact]
    public void Find_OverlappingPrefixes_PrefersTheLongest()
    {
        // Act
        var preset = ModelPreset.Find("gpt-4.1-mini");

        // Assert
        preset.Should().NotBeNull();
        preset.ContextWindow.Should().Be(1_047_576);
    }

    [Fact]
    public void Find_UnknownModel_ReturnsNull()
    {
        // Act
        var preset = ModelPreset.Find("my-local-model");

        // Assert
        preset.Should().BeNull();
    }
}
