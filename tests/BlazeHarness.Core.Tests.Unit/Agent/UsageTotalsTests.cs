using BlazeHarness.Core.Agent;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class UsageTotalsTests
{
    [Fact]
    public void Of_TimedResponses_AddsTokensAndComputesSpeeds()
    {
        // Arrange
        var usages = new[]
        {
            new UsageReported(1000, 101, 400, TimeToFirstToken: TimeSpan.FromSeconds(2), GenerationTime: TimeSpan.FromSeconds(10)),
            new UsageReported(500, 51, 0, TimeToFirstToken: TimeSpan.FromSeconds(1), GenerationTime: TimeSpan.FromSeconds(5)),
        };

        // Act
        var totals = UsageTotals.Of(usages);

        // Assert
        totals.Responses.Should().Be(2);
        totals.PromptTokens.Should().Be(1500);
        totals.CacheHitRate.Should().BeApproximately(400d / 1500, 1e-9);
        totals.DecodeTokensPerSecond.Should().BeApproximately(10, 1e-9);
        totals.PrefillTokensPerSecond.Should().BeApproximately(1100d / 3, 1e-9);
        totals.TimeToFirstToken.Should().Be(TimeSpan.FromSeconds(1.5));
        totals.GenerationTime.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void Of_UntimedResponses_LeavesSpeedsNull()
    {
        // Arrange
        var usages = new[] { new UsageReported(10, 4, 2) };

        // Act
        var totals = UsageTotals.Of(usages);

        // Assert
        totals.DecodeTokensPerSecond.Should().BeNull();
        totals.PrefillTokensPerSecond.Should().BeNull();
        totals.TimeToFirstToken.Should().BeNull();
    }
}
