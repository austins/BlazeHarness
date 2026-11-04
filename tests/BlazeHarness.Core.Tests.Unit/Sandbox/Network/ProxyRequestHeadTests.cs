using BlazeHarness.Core.Sandbox.Network;
using System.Text;

namespace BlazeHarness.Core.Tests.Unit.Sandbox.Network;

public sealed class ProxyRequestHeadTests
{
    [Theory]
    [InlineData("CONNECT Registry.NPMJS.org.:443 HTTP/1.1\r\nHost: registry.npmjs.org:443", "registry.npmjs.org", 443)]
    [InlineData("CONNECT [2001:db8::1]:8443 HTTP/1.1", "2001:db8::1", 8443)]
    public void TryParse_Connect_ReadsHostAndPort(string head, string host, int port)
    {
        // Act
        var parsed = ProxyRequestHead.TryParse(head);

        // Assert
        parsed.Should().NotBeNull();
        (parsed.IsTunnel, parsed.Host, parsed.Port).Should().Be((true, host, port));
    }

    [Fact]
    public void TryParse_AbsoluteUri_RewritesToOriginFormAndDropsProxyHeaders()
    {
        // Arrange
        const string Head = "GET http://Example.com:8080/a/b?q=1 HTTP/1.1\r\nHost: example.com:8080\r\nProxy-Connection: keep-alive\r\nProxy-Authorization: Basic x\r\nAccept: */*";

        // Act
        var parsed = ProxyRequestHead.TryParse(Head);

        // Assert
        parsed.Should().NotBeNull();
        (parsed.IsTunnel, parsed.Host, parsed.Port).Should().Be((false, "example.com", 8080));
        var text = Encoding.ASCII.GetString(parsed.Forward);
        text.Should().StartWith("GET /a/b?q=1 HTTP/1.1\r\n").And.Contain("Accept: */*").And.Contain("Connection: close");
        text.Should().NotContain("Proxy-").And.NotContain("http://");
    }

    [Fact]
    public void TryParse_AbsoluteUriWithoutPort_UsesPort80()
    {
        // Act
        var parsed = ProxyRequestHead.TryParse("GET http://example.com HTTP/1.1\r\nHost: example.com");

        // Assert
        parsed.Should().NotBeNull();
        parsed.Port.Should().Be(80);
        Encoding.ASCII.GetString(parsed.Forward).Should().StartWith("GET / HTTP/1.1\r\n");
    }

    [Theory]
    [InlineData("GET /relative HTTP/1.1\r\nHost: example.com")]
    [InlineData("GET https://example.com/ HTTP/1.1")]
    [InlineData("CONNECT example.com HTTP/1.1")]
    [InlineData("CONNECT example.com:abc HTTP/1.1")]
    [InlineData("CONNECT example.com:99999 HTTP/1.1")]
    [InlineData("nonsense")]
    [InlineData("GET http://example.com/ SPDY/3")]
    public void TryParse_Unsupported_ReturnsNull(string head)
    {
        // Act
        var parsed = ProxyRequestHead.TryParse(head);

        // Assert
        parsed.Should().BeNull();
    }
}
