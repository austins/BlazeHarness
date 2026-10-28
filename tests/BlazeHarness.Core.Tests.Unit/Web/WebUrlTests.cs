using BlazeHarness.Core.Web;

namespace BlazeHarness.Core.Tests.Unit.Web;

public sealed class WebUrlTests
{
    [Theory]
    [InlineData("ftp://example.com/x")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:pw@example.com/")]
    [InlineData("example.com")]
    [InlineData("")]
    public void TryParse_Unsupported_False(string text)
    {
        // Act
        var ok = WebUrl.TryParse(text, out _, out var error);

        // Assert
        ok.Should().BeFalse();
        error.Should().NotBeEmpty();
    }

    [Fact]
    public void SameSite_WwwDifference_True()
    {
        // Act
        var same = WebUrl.SameSite(new Uri("https://Example.com./a"), new Uri("http://example.com/b"));

        // Assert
        same.Should().BeTrue();
    }

    [Fact]
    public void SameSite_Subdomain_False()
    {
        // Act
        var same = WebUrl.SameSite(new Uri("https://example.com"), new Uri("https://docs.example.com"));

        // Assert
        same.Should().BeFalse();
    }
}
