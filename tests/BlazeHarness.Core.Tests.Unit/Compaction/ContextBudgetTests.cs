using BlazeHarness.Core.Compaction;
using BlazeHarness.Core.Providers;

namespace BlazeHarness.Core.Tests.Unit.Compaction;

public sealed class ContextBudgetTests
{
    [Fact]
    public void Limit_IsAFractionOfTheWindowLeftAfterTheResponse()
    {
        // Arrange
        var model = new ModelConfig { Id = "m", ContextWindow = 100_000, MaxOutputTokens = 10_000 };

        // Act
        var result = ContextBudget.Limit(model);

        // Assert
        result.Should().Be(72_000);
    }

    [Fact]
    public void Limit_WithoutAKnownWindow_IsZero()
    {
        // Arrange
        var model = new ModelConfig { Id = "m", ContextWindow = 0, MaxOutputTokens = 4_000 };

        // Act
        var result = ContextBudget.Limit(model);

        // Assert
        result.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(60_000)]
    public void Limit_WithNoOutputCapOrOneThatLeavesNoRoom_ReservesHalfTheWindow(int maxOutput)
    {
        // Arrange
        var model = new ModelConfig { Id = "m", ContextWindow = 100_000, MaxOutputTokens = maxOutput };

        // Act
        var result = ContextBudget.Limit(model);

        // Assert
        result.Should().Be(40_000);
    }
}
