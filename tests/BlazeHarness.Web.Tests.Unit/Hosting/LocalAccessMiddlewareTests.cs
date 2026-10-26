using BlazeHarness.Web.Hosting;
using Microsoft.AspNetCore.Http;

namespace BlazeHarness.Web.Tests.Unit.Hosting;

public sealed class LocalAccessMiddlewareTests
{
    private const int Port = 5555;

    private readonly LaunchCredentials _credentials = new();
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
            _credentials);
    }

    [Fact]
    public async Task InvokeAsync_ValidCookieAndHost_CallsNext()
    {
        // Arrange
        var context = CreateContext("127.0.0.1:5555", cookie: _credentials.SessionSecret);

        // Act
        await _sut.InvokeAsync(context);

        // Assert
        _nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Theory]
    [InlineData("evil.com")]
    [InlineData("127.0.0.1:9999")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1.evil.com:5555")]
    public async Task InvokeAsync_UnexpectedHost_Returns403(string host)
    {
        // Arrange
        var context = CreateContext(host, cookie: _credentials.SessionSecret);

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
        var context = CreateContext("127.0.0.1:5555", cookie: _credentials.SessionSecret, origin: origin);

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
        var context = CreateContext("localhost:5555", cookie: _credentials.SessionSecret, origin: "http://localhost:5555");

        // Act
        await _sut.InvokeAsync(context);

        // Assert
        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_SignalRWithoutOrigin_Returns403()
    {
        // Arrange
        var context = CreateContext("127.0.0.1:5555", cookie: _credentials.SessionSecret, path: "/_blazor");

        // Act
        await _sut.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(403);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    public async Task InvokeAsync_MissingOrWrongCookie_Returns401(string? cookie)
    {
        // Arrange
        var context = CreateContext("127.0.0.1:5555", cookie: cookie);

        // Act
        await _sut.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(401);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_LaunchWithToken_SetsStrictHttpOnlyCookieAndRedirects()
    {
        // Arrange
        var context = CreateContext("127.0.0.1:5555", path: "/launch", query: "?token=" + _credentials.Token);

        // Act
        await _sut.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(302);
        var setCookie = context.Response.Headers.SetCookie.ToString();
        setCookie.Should().Contain(LaunchCredentials.CookieName + "=" + _credentials.SessionSecret);
        setCookie.Should().ContainEquivalentOf("httponly").And.ContainEquivalentOf("samesite=strict");
        setCookie.Should().NotContain(_credentials.Token);
    }

    [Fact]
    public async Task InvokeAsync_LaunchTokenReused_Returns401()
    {
        // Arrange
        await _sut.InvokeAsync(CreateContext("127.0.0.1:5555", path: "/launch", query: "?token=" + _credentials.Token));
        var second = CreateContext("127.0.0.1:5555", path: "/launch", query: "?token=" + _credentials.Token);

        // Act
        await _sut.InvokeAsync(second);

        // Assert
        second.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task InvokeAsync_LaunchWithWrongToken_Returns401()
    {
        // Arrange
        var context = CreateContext("127.0.0.1:5555", path: "/launch", query: "?token=nope");

        // Act
        await _sut.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(401);
        context.Response.Headers.SetCookie.ToString().Should().BeEmpty();
    }

    private static DefaultHttpContext CreateContext(
        string host,
        string? cookie = null,
        string? origin = null,
        string path = "/",
        string query = "")
    {
        var context = new DefaultHttpContext();
        context.Connection.LocalPort = Port;
        context.Request.Method = "GET";
        context.Request.Host = new HostString(host);
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        context.Response.Body = new MemoryStream();
        if (origin is not null)
        {
            context.Request.Headers.Origin = origin;
        }

        if (cookie is not null)
        {
            context.Request.Headers.Cookie = $"{LaunchCredentials.CookieName}={cookie}";
        }

        return context;
    }
}
