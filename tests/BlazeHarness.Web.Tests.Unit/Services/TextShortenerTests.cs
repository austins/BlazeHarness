using BlazeHarness.Web.Services;

namespace BlazeHarness.Web.Tests.Unit.Services;

public sealed class TextShortenerTests
{
    [Fact]
    public void Shorten_TextThatFits_ReturnsItUnchanged()
    {
        // Arrange
        const string text = "short";

        // Act
        var result = TextShortener.Shorten(text, 5);

        // Assert
        result.Should().Be(text);
    }

    [Fact]
    public void Shorten_LongText_CutsAndAddsEllipsis()
    {
        // Arrange
        const string text = "abcdefgh";

        // Act
        var result = TextShortener.Shorten(text, 3);

        // Assert
        result.Should().Be("abc…");
    }

    [Fact]
    public void Shorten_CutInsideSurrogatePair_DropsTheHalfPair()
    {
        // Arrange
        const string text = "ab😀cd";

        // Act
        var result = TextShortener.Shorten(text, 3);

        // Assert
        result.Should().Be("ab…");
    }
}
