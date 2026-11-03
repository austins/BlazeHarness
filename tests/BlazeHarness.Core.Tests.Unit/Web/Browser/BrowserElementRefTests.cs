using BlazeHarness.Core.Web.Browser;

namespace BlazeHarness.Core.Tests.Unit.Web.Browser;

public sealed class BrowserElementRefTests
{
    [Fact]
    public void Format_NodeId_PrefixesE()
    {
        // Act
        var result = BrowserElementRef.Format(42);

        // Assert
        result.Should().Be("e42");
    }

    [Theory]
    [InlineData("e42", 42)]
    [InlineData("e1", 1)]
    public void TryParse_Ref_ReturnsNodeId(string text, long expected)
    {
        // Act
        var parsed = BrowserElementRef.TryParse(text, out var id);

        // Assert
        parsed.Should().BeTrue();
        id.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("e")]
    [InlineData("e0")]
    [InlineData("42")]
    [InlineData("e-3")]
    [InlineData("e4x")]
    [InlineData("button")]
    public void TryParse_NotARef_ReturnsFalse(string? text)
    {
        // Act
        var parsed = BrowserElementRef.TryParse(text, out _);

        // Assert
        parsed.Should().BeFalse();
    }
}
