using BlazeHarness.Core.Web.Browser;

namespace BlazeHarness.Core.Tests.Unit.Web.Browser;

public sealed class BrowserAddressPolicyTests
{
    [Theory]
    [InlineData("http://localhost:3000/")]
    [InlineData("https://localhost/")]
    [InlineData("http://127.0.0.1:8080/a?b=1")]
    [InlineData("http://127.5.6.7/")]
    [InlineData("http://[::1]:5000/")]
    [InlineData("http://app.localhost:3000/")]
    [InlineData("http://LOCALHOST:3000/")]
    public void Refusal_LoopbackPage_IsAllowed(string address)
    {
        // Act
        var result = BrowserAddressPolicy.Refusal(new Uri(address), 5380);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("http://192.168.1.10:3000/")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://169.254.169.254/")]
    [InlineData("http://localhost.evil.com/")]
    [InlineData("http://[::ffff:8.8.8.8]/")]
    public void Refusal_NotThisComputer_IsRefused(string address)
    {
        // Act
        var result = BrowserAddressPolicy.Refusal(new Uri(address), 5380);

        // Assert
        result.Should().Contain("this computer");
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://localhost/")]
    [InlineData("data:text/html,hi")]
    public void Refusal_OtherScheme_IsRefused(string address)
    {
        // Act
        var result = BrowserAddressPolicy.Refusal(new Uri(address), null);

        // Assert
        result.Should().Contain("http");
    }

    [Theory]
    [InlineData("http://localhost:5380/")]
    [InlineData("http://127.0.0.1:5380/_blazor")]
    [InlineData("http://[::1]:5380/")]
    public void Refusal_AppPort_IsRefused(string address)
    {
        // Act
        var result = BrowserAddressPolicy.Refusal(new Uri(address), 5380);

        // Assert
        result.Should().Contain("BlazeHarness");
    }

    [Fact]
    public void Refusal_UnknownAppPort_AllowsAnyLoopbackPort()
    {
        // Act
        var result = BrowserAddressPolicy.Refusal(new Uri("http://localhost:5380/"), null);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("http://LocalHost:3000/x", "localhost:3000")]
    [InlineData("http://localhost/", "localhost:80")]
    [InlineData("https://localhost/", "localhost:443")]
    [InlineData("http://[::1]:5000/", "[::1]:5000")]
    public void OriginOf_Address_GivesHostAndPort(string address, string expected)
    {
        // Act
        var result = BrowserAddressPolicy.OriginOf(new Uri(address));

        // Assert
        result.Should().Be(expected);
    }
}
