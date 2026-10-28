using BlazeHarness.Web.Hosting;
using Microsoft.AspNetCore.Http;

namespace BlazeHarness.Web.Tests.Unit.Hosting;

public sealed class LocalAccessMiddlewareTests
{
    private const int Port = 5555;

    private readonly LocalAccessMiddleware _sut;
    private bool _nextCalled;

    public LocalAccessMiddlewareTests()
    {
        _sut = new LocalAccessMiddleware(
            _ =>
            {
                _nextCalled = true;
                return Task.CompletedTask;
            },
            new AccessPolicy(AllowAnyHost: false));
    }

    [Fact]
    public async Task InvokeAsync_ValidHost_CallsNext()
    {
        // Arrange
        var context = CreateContext("127.0.0.1:5555");

        // Act
        await _sut.InvokeAsync(context);

        // Assert
        _nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_ValidRequest_SetsSecurityHeaders()
    {
        // Arrange
        var context = CreateContext("127.0.0.1:5555");

        // Act
        await _sut.InvokeAsync(context);

        // Assert
        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy.ToString().Should().Be(LocalAccessMiddleware.PageContentSecurityPolicy).And.Contain("frame-ancestors 'none'");
        headers.XContentTypeOptions.ToString().Should().Be("nosniff");
        headers.XFrameOptions.ToString().Should().Be("DENY");
        headers["Referrer-Policy"].ToString().Should().Be("no-referrer");
    }

    [Theory]
    [InlineData("evil.com")]
    [InlineData("127.0.0.1:9999")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1.evil.com:5555")]
    [InlineData("192.168.1.20:5555")]
    [InlineData("[::1]:5555")]
    public async Task InvokeAsync_UnexpectedHost_Returns403(string host)
    {
        // Arrange
        var context = CreateContext(host);

        // Act
        await _sut.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(403);
        _nextCalled.Should().BeFalse();
    }

    [Theory]
    [InlineData("http://evil.com")]
    [InlineData("null")]
    [InlineData("https://127.0.0.1:5555")]
    [InlineData("http://127.0.0.1:5556")]
    public async Task InvokeAsync_ForeignOrigin_Returns403(string origin)
    {
        // Arrange
        var context = CreateContext("127.0.0.1:5555", origin: origin);

        // Act
        await _sut.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(403);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_SameOrigin_CallsNext()
    {
        // Arrange
        var context = CreateContext("localhost:5555", origin: "http://localhost:5555");

        // Act
        await _sut.InvokeAsync(context);

        // Assert
        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_SignalRNegotiateWithoutOrigin_Returns403()
    {
        // Arrange
        var context = CreateContext("127.0.0.1:5555", path: "/_blazor/negotiate", method: "POST");

        // Act
        await _sut.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_BlazorInitializersGetWithoutOrigin_CallsNext()
    {
        // Arrange
        var context = CreateContext("127.0.0.1:5555", path: "/_blazor/initializers");

        // Act
        await _sut.InvokeAsync(context);

        // Assert
        _nextCalled.Should().BeTrue();
    }

    [Theory]
    [InlineData("192.168.1.20:5555")]
    [InlineData("[fe80::1]:5555")]
    [InlineData("localhost:5555")]
    public async Task InvokeAsync_IpHostWhenAllInterfaces_CallsNext(string host)
    {
        // Arrange
        var sut = CreateAllInterfaces();
        var context = CreateContext(host, origin: "http://" + host);

        // Act
        await sut.InvokeAsync(context);

        // Assert
        _nextCalled.Should().BeTrue();
    }

    [Theory]
    [InlineData("evil.com:5555")]
    [InlineData("mypc.local:5555")]
    [InlineData("192.168.1.20:9999")]
    [InlineData("192.168.1.20")]
    public async Task InvokeAsync_DomainOrWrongPortWhenAllInterfaces_Returns403(string host)
    {
        // Arrange
        var sut = CreateAllInterfaces();
        var context = CreateContext(host);

        // Act
        await sut.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(403);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_OriginDiffersFromHostWhenAllInterfaces_Returns403()
    {
        // Arrange
        var sut = CreateAllInterfaces();
        var context = CreateContext("192.168.1.20:5555", origin: "http://192.168.1.21:5555");

        // Act
        await sut.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(403);
    }

    private LocalAccessMiddleware CreateAllInterfaces() => new(
        _ =>
        {
            _nextCalled = true;
            return Task.CompletedTask;
        },
        new AccessPolicy(AllowAnyHost: true));

    private static DefaultHttpContext CreateContext(
        string host,
        string? origin = null,
        string path = "/",
        string method = "GET")
    {
        var context = new DefaultHttpContext();
        context.Connection.LocalPort = Port;
        context.Request.Method = method;
        context.Request.Host = new HostString(host);
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        if (origin is not null)
        {
            context.Request.Headers.Origin = origin;
        }

        return context;
    }
}
