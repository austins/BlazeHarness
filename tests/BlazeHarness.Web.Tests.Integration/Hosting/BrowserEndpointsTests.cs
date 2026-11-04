using BlazeHarness.Web.Hosting;

namespace BlazeHarness.Web.Tests.Integration.Hosting;

public sealed class BrowserEndpointsTests
{
    [Fact]
    public void StreamAddressOf_ChatId_IsRelativeAndMatchesTheRoute()
    {
        // Act
        var result = BrowserEndpoints.StreamAddressOf("0123abcd");

        // Assert
        result.Should().Be("api/chats/0123abcd/browser/stream");
        BrowserEndpoints.StreamRoute.Should().Be("/" + result.Replace("0123abcd", "{chatId}", StringComparison.Ordinal));
    }

    [Fact]
    public void StreamAddressOf_ChatIdWithSlash_IsEscaped()
    {
        // Act
        var result = BrowserEndpoints.StreamAddressOf("a/b");

        // Assert
        result.Should().Be("api/chats/a%2Fb/browser/stream");
    }

    [Fact]
    public void ScreenshotAddressOf_ChatId_IsRelativeAndMatchesTheRoute()
    {
        // Act
        var result = BrowserEndpoints.ScreenshotAddressOf("0123abcd");

        // Assert
        result.Should().Be("api/chats/0123abcd/browser/screenshot");
        BrowserEndpoints.ScreenshotRoute.Should().Be("/" + result.Replace("0123abcd", "{chatId}", StringComparison.Ordinal));
    }
}
