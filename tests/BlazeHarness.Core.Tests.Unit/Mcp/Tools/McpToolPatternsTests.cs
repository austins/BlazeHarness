using BlazeHarness.Core.Mcp.Tools;

namespace BlazeHarness.Core.Tests.Unit.Mcp.Tools;

public sealed class McpToolPatternsTests
{
    [Theory]
    [InlineData("*", "anything", true)]
    [InlineData("get_*", "get_thread", true)]
    [InlineData("get_*", "set_thread", false)]
    [InlineData("ping", "ping", true)]
    [InlineData("ping", "pings", false)]
    [InlineData("PING", "ping", false)]
    public void Matches_OnePattern_FollowsTheWildcardRules(string pattern, string tool, bool expected)
    {
        // Act
        var matches = McpToolPatterns.Matches([pattern], tool);

        // Assert
        matches.Should().Be(expected);
    }

    [Fact]
    public void Matches_NoPatterns_MatchesNothing()
    {
        // Act
        var matches = McpToolPatterns.Matches([], "ping");

        // Assert
        matches.Should().BeFalse();
    }
}
