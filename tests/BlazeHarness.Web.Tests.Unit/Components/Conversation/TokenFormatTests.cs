using BlazeHarness.Web.Components.Conversation;
using System.Globalization;

namespace BlazeHarness.Web.Tests.Unit.Components.Conversation;

public sealed class TokenFormatTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(950, "950")]
    [InlineData(1000, "1k")]
    [InlineData(4200, "4.2k")]
    [InlineData(9999, "10k")]
    [InlineData(10_000, "10k")]
    [InlineData(128_000, "128k")]
    public void Of_ShortensLargeCounts(long tokens, string expected)
    {
        // Arrange
        using var _ = new InvariantCulture();

        // Act
        var text = TokenFormat.Of(tokens);

        // Assert
        text.Should().Be(expected);
    }

    [Fact]
    public void Elapsed_UnderAMinute_ShowsWholeSeconds()
    {
        // Arrange
        using var _ = new InvariantCulture();

        // Act
        var text = TokenFormat.Elapsed(TimeSpan.FromSeconds(7.9));

        // Assert
        text.Should().Be("7 s");
    }

    [Fact]
    public void Elapsed_AMinuteOrMore_UsesTheDurationFormat()
    {
        // Act
        var text = TokenFormat.Elapsed(TimeSpan.FromSeconds(90));

        // Assert
        text.Should().Be(TokenFormat.Duration(TimeSpan.FromSeconds(90)));
    }

    private sealed class InvariantCulture : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public InvariantCulture()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _previous;
        }
    }
}
