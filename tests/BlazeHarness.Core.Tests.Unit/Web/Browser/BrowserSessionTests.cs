using BlazeHarness.Core.Web.Browser;
using PuppeteerSharp;

namespace BlazeHarness.Core.Tests.Unit.Web.Browser;

public sealed class BrowserSessionTests
{
    private static readonly Uri Page = new("https://example.com/");
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(1);
    private readonly DateTimeOffset _start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly BrowserSession _sut;

    public BrowserSessionTests()
    {
        _sut = new BrowserSession(
            Substitute.For<IBrowserContext>(),
            Substitute.For<IPage>(),
            Substitute.For<ICDPSession>(),
            Substitute.For<ICDPSession>(),
            _start);
    }

    [Fact]
    public void FreshTitle_NothingRemembered_IsNull()
    {
        // Act
        var result = _sut.FreshTitle(Page, _start, MaxAge);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void FreshTitle_SameAddressWithinMaxAge_IsTheRememberedTitle()
    {
        // Arrange
        _sut.RememberTitle(Page, "Example", _start);

        // Act
        var result = _sut.FreshTitle(Page, _start.AddMilliseconds(500), MaxAge);

        // Assert
        result.Should().Be("Example");
    }

    [Fact]
    public void FreshTitle_OlderThanMaxAge_IsNull()
    {
        // Arrange
        _sut.RememberTitle(Page, "Example", _start);

        // Act
        var result = _sut.FreshTitle(Page, _start.AddSeconds(1), MaxAge);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void FreshTitle_OtherAddress_IsNull()
    {
        // Arrange
        _sut.RememberTitle(Page, "Example", _start);

        // Act
        var result = _sut.FreshTitle(new Uri("https://example.org/"), _start, MaxAge);

        // Assert
        result.Should().BeNull();
    }
}
