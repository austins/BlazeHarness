using BlazeHarness.Core.Web.Browser;
using BlazeHarness.Tests.Shared;
using Microsoft.Extensions.Time.Testing;
using System.Net;
using System.Net.Sockets;

namespace BlazeHarness.Core.Tests.Integration.Web.Browser;

public sealed class PuppeteerBrowserEngineTests : IDisposable
{
    private const string Chat = "chat1";
    private const string Reason = "Needs a Chrome program; set BLAZEHARNESS_TEST_CHROME or install Playwright's Chromium.";

    private readonly HttpListener _listener = new();
    private readonly TempFolder _folder = new("bh-browser-");
    private readonly CancellationTokenSource _stop = new();
    private readonly int _port;

    public PuppeteerBrowserEngineTests()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        _port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        _listener.Start();
        _ = Task.Run(ServeAsync, _stop.Token);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Close();
        _stop.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public async Task IsInstalledAsync_NothingDownloaded_ReturnsFalse()
    {
        // Arrange
        using var sut = Create(executablePath: null);

        // Act
        var result = await sut.IsInstalledAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task NavigateAsync_NotInstalled_ExplainsHowToInstall()
    {
        // Arrange
        using var sut = Create(executablePath: null);

        // Act
        var act = async () => await sut.NavigateAsync(Chat, Url("/"), TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<BrowserException>().WithMessage("*Install browser*");
    }

    [Fact]
    public async Task NavigateAsync_AppPort_Refused()
    {
        // Arrange
        using var sut = Create(executablePath: null, appPort: _port);

        // Act
        var act = async () => await sut.NavigateAsync(Chat, Url("/"), TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<BrowserException>().WithMessage("*BlazeHarness*");
    }

    [Fact]
    public async Task SnapshotAsync_NothingOpened_Throws()
    {
        // Arrange
        using var sut = Create();

        // Act
        var act = async () => await sut.SnapshotAsync(Chat, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<BrowserException>().WithMessage("*browser_navigate*");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task NavigateAsync_Page_ReadsTitleStatusAndOutline()
    {
        // Arrange
        using var sut = Create();

        // Act
        var view = await sut.NavigateAsync(Chat, Url("/"), TestContext.Current.CancellationToken).Bounded();

        // Assert
        view.Title.Should().Be("Demo page");
        view.Status.Should().Be(200);
        view.Snapshot.Should().Contain("heading \"Welcome\" [level=1]").And.Contain("button \"Save\"").And.Contain("link \"Docs\"");
        view.Problems.Should().BeEmpty();
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task NavigateAsync_ScriptRendersContent_ShowsRenderedText()
    {
        // Arrange
        using var sut = Create();

        // Act
        var view = await sut.NavigateAsync(Chat, Url("/script"), TestContext.Current.CancellationToken).Bounded();

        // Assert
        view.Snapshot.Should().Contain("Rendered by script");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task NavigateAsync_PageWithErrors_ReportsThem()
    {
        // Arrange
        using var sut = Create();

        // Act
        var view = await sut.NavigateAsync(Chat, Url("/broken"), TestContext.Current.CancellationToken).Bounded();

        // Assert
        view.Problems.Should().Contain(problem => problem.Contains("console error: boom", StringComparison.Ordinal))
            .And.Contain(problem => problem.StartsWith("404 GET", StringComparison.Ordinal));
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task SnapshotAsync_AfterNavigate_ReadsSamePage()
    {
        // Arrange
        using var sut = Create();
        await sut.NavigateAsync(Chat, Url("/"), TestContext.Current.CancellationToken).Bounded();

        // Act
        var view = await sut.SnapshotAsync(Chat, TestContext.Current.CancellationToken).Bounded();

        // Assert
        view.Title.Should().Be("Demo page");
        view.Status.Should().BeNull();
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task NavigateAsync_NothingListening_ExplainsWhy()
    {
        // Arrange
        using var sut = Create();
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var closedPort = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        // Act
        var act = async () => await sut.NavigateAsync(Chat, new Uri($"http://127.0.0.1:{closedPort}/"), TestContext.Current.CancellationToken).Bounded();

        // Assert
        await act.Should().ThrowAsync<BrowserException>().WithMessage("*ERR_CONNECTION_REFUSED*");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task NavigateAsync_RedirectToOtherOrigin_Blocked()
    {
        // Arrange
        using var sut = Create();

        // Act
        var act = async () => await sut.NavigateAsync(Chat, Url("/redirect"), TestContext.Current.CancellationToken).Bounded();

        // Assert
        await act.Should().ThrowAsync<BrowserException>().WithMessage("*may not open*");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task NavigateAsync_SubresourceOutsideLocalhost_Blocked()
    {
        // Arrange
        using var sut = Create();

        // Act
        var view = await sut.NavigateAsync(Chat, Url("/external"), TestContext.Current.CancellationToken).Bounded();

        // Assert
        view.Problems.Should().Contain(problem => problem.Contains("192.0.2.1:81", StringComparison.Ordinal));
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task NavigateAsync_TwoChats_ShareNoCookies()
    {
        // Arrange
        using var sut = Create();
        await sut.NavigateAsync("first", Url("/cookie"), TestContext.Current.CancellationToken).Bounded();

        // Act
        var second = await sut.NavigateAsync("second", Url("/whoami"), TestContext.Current.CancellationToken).Bounded();
        var first = await sut.NavigateAsync("first", Url("/whoami"), TestContext.Current.CancellationToken).Bounded();

        // Assert
        second.Snapshot.Should().Contain("cookie: none");
        first.Snapshot.Should().Contain("cookie: seen=1");
    }

    private static string Html(string title, string body) => $"<!doctype html><html><head><title>{title}</title></head><body>{body}</body></html>";

    private PuppeteerBrowserEngine Create(string? executablePath = "default", int? appPort = null) =>
        new(
            appPort,
            _folder.Root,
            new LoggerFake<PuppeteerBrowserEngine>(),
            new FakeTimeProvider(),
            executablePath: executablePath == "default" ? TestChrome.Path : executablePath);

    private Uri Url(string path) => new($"http://127.0.0.1:{_port}{path}");

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            var response = context.Response;
            var body = string.Empty;
            response.ContentType = "text/html";
            switch (context.Request.Url!.AbsolutePath)
            {
                case "/":
                    body = Html("Demo page", "<h1>Welcome</h1><button>Save</button><a href=\"/docs\">Docs</a>");
                    break;
                case "/script":
                    body = Html("Script", "<div id=\"out\"></div><script>document.getElementById('out').textContent = 'Rendered by script';</script>");
                    break;
                case "/broken":
                    body = Html("Broken", "<script>console.error('boom');</script><script src=\"/missing.js\"></script>");
                    break;
                case "/redirect":
                    response.StatusCode = 302;
                    response.RedirectLocation = $"http://localhost:{_port}/";
                    break;
                case "/external":
                    body = Html("External", "<img src=\"http://192.0.2.1:81/x.png\">");
                    break;
                case "/cookie":
                    response.AppendHeader("Set-Cookie", "seen=1; Path=/");
                    body = Html("Cookie", "<p>set</p>");
                    break;
                case "/whoami":
                    var cookie = context.Request.Cookies["seen"] is { } seen ? "seen=" + seen.Value : "none";
                    body = Html("Who", $"<p>cookie: {cookie}</p>");
                    break;
                default:
                    response.StatusCode = 404;
                    break;
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(body);
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, _stop.Token);
            response.Close();
        }
    }
}
