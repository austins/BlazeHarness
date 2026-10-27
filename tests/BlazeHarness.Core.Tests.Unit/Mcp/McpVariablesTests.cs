using BlazeHarness.Core.Mcp;

namespace BlazeHarness.Core.Tests.Unit.Mcp;

public sealed class McpVariablesTests
{
    private static readonly Dictionary<string, string> Environment = new() { ["HOME"] = "/home/a", ["EMPTY"] = string.Empty, ["TOKEN"] = "t0k" };

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("${HOME}/x", "/home/a/x")]
    [InlineData("a${HOME}b${TOKEN}c", "a/home/ab" + "t0k" + "c")]
    [InlineData("${MISSING:-fallback}", "fallback")]
    [InlineData("${EMPTY:-fallback}", "fallback")]
    [InlineData("${HOME:-fallback}", "/home/a")]
    [InlineData("${MISSING:-}", "")]
    [InlineData("${EMPTY}", "")]
    [InlineData("${MISSING:-a:-b}", "a:-b")]
    public void TryExpand_ReplacesReferences(string text, string expected)
    {
        // Act
        var ok = McpVariables.TryExpand(text, Lookup, out var result, out var missing);

        // Assert
        ok.Should().BeTrue();
        result.Should().Be(expected);
        missing.Should().BeNull();
    }

    [Theory]
    [InlineData("${")]
    [InlineData("${}")]
    [InlineData("${1A}")]
    [InlineData("${a b}")]
    [InlineData("$HOME")]
    [InlineData("${HOME")]
    [InlineData("$")]
    public void TryExpand_NotAReference_IsLeftAsItIs(string text)
    {
        // Act
        var ok = McpVariables.TryExpand(text, Lookup, out var result, out _);

        // Assert
        ok.Should().BeTrue();
        result.Should().Be(text);
    }

    [Fact]
    public void TryExpand_UnsetWithoutDefault_NamesTheVariable()
    {
        // Act
        var ok = McpVariables.TryExpand("x ${HOME} ${NOPE} ${ALSO}", Lookup, out _, out var missing);

        // Assert
        ok.Should().BeFalse();
        missing.Should().Be("NOPE");
    }

    [Fact]
    public void TryExpand_TheValueOfAVariable_IsNotExpandedAgain()
    {
        // Act
        var ok = McpVariables.TryExpand("${A}", name => name == "A" ? "${B}" : "boom", out var result, out _);

        // Assert
        ok.Should().BeTrue();
        result.Should().Be("${B}");
    }

    private static string? Lookup(string name) => Environment.GetValueOrDefault(name);
}
