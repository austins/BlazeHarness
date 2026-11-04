using BlazeHarness.Core.Web.Browser;
using BlazeHarness.Tests.Shared;
using Microsoft.Extensions.Time.Testing;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace BlazeHarness.Core.Tests.Integration.Web.Browser;

public sealed class PuppeteerBrowserEngineTests : IDisposable
{
    private const string Chat = "chat1";
    private const string Reason = "Needs a Chrome program; set BLAZEHARNESS_TEST_CHROME or install Playwright's Chromium.";

    private readonly HttpListener _listener;
    private readonly TempFolder _folder = new("bh-browser-");
    private readonly CancellationTokenSource _stop = new();
    private readonly int _port;

    public PuppeteerBrowserEngineTests()
    {
        (_listener, _port) = LocalHttpListener.Start("127.0.0.1", "localhost");
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
    public async Task NavigateAsync_Page_PublishesJpegFrame()
    {
        // Arrange
        using var sut = Create();
        var first = new TaskCompletionSource<BrowserFrameChangedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.FrameChanged += (_, args) => first.TrySetResult(args);

        // Act
        await sut.NavigateAsync(Chat, Url("/"), TestContext.Current.CancellationToken).Bounded();
        var args = await first.Task.Bounded();

        // Assert
        args.ChatId.Should().Be(Chat);
        args.Frame.Should().NotBeNull();
        args.Frame.Jpeg.Span[..2].ToArray().Should().Equal(0xFF, 0xD8);
        args.Frame.Url.Should().Be(Url("/"));
        sut.LatestFrame(Chat).Should().NotBeNull();
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task Forget_PageOpen_PublishesClosedAndClearsLatestFrame()
    {
        // Arrange
        using var sut = Create();
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.FrameChanged += (_, args) => (args.Frame is null ? closed : shown).TrySetResult();
        await sut.NavigateAsync(Chat, Url("/"), TestContext.Current.CancellationToken).Bounded();

        // Act
        await shown.Task.Bounded();
        sut.Forget(Chat);
        await closed.Task.Bounded();

        // Assert
        sut.LatestFrame(Chat).Should().BeNull();
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

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task NavigateAsync_Controls_ShowRefs()
    {
        // Arrange
        using var sut = Create();

        // Act
        var view = await sut.NavigateAsync(Chat, Url("/form"), TestContext.Current.CancellationToken).Bounded();

        // Assert
        view.Snapshot.Should().MatchRegex("button \"Greet\" \\[ref=e\\d+\\]").And.MatchRegex("textbox \"Name\" \\[ref=e\\d+\\]");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task ClickAsync_Button_RunsItsScriptAndShowsTheResult()
    {
        // Arrange
        using var sut = Create();
        var page = await sut.NavigateAsync(Chat, Url("/form"), TestContext.Current.CancellationToken).Bounded();

        // Act
        var view = await sut.ClickAsync(Chat, RefOf(page, "button \"Greet\""), TestContext.Current.CancellationToken).Bounded();

        // Assert
        view.Snapshot.Should().Contain("Hello r");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task TypeAsync_FieldAndDropDown_FillsThemForTheNextClick()
    {
        // Arrange
        using var sut = Create();
        var page = await sut.NavigateAsync(Chat, Url("/form"), TestContext.Current.CancellationToken).Bounded();

        // Act
        await sut.TypeAsync(Chat, RefOf(page, "textbox \"Name\""), "Ada", submit: false, TestContext.Current.CancellationToken).Bounded();
        await sut.TypeAsync(Chat, RefOf(page, "combobox \"Color\""), "Green", submit: false, TestContext.Current.CancellationToken).Bounded();
        var view = await sut.ClickAsync(Chat, RefOf(page, "button \"Greet\""), TestContext.Current.CancellationToken).Bounded();

        // Assert
        view.Snapshot.Should().Contain("Hello Ada g");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task TypeAsync_Button_ExplainsItDoesNotTakeText()
    {
        // Arrange
        using var sut = Create();
        var page = await sut.NavigateAsync(Chat, Url("/form"), TestContext.Current.CancellationToken).Bounded();

        // Act
        var act = async () => await sut.TypeAsync(Chat, RefOf(page, "button \"Greet\""), "x", submit: false, TestContext.Current.CancellationToken).Bounded();

        // Assert
        await act.Should().ThrowAsync<BrowserException>().WithMessage("*browser_click*");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task ClickAsync_RefOfRemovedElement_AsksForFreshRefs()
    {
        // Arrange
        using var sut = Create();
        await sut.NavigateAsync(Chat, Url("/form"), TestContext.Current.CancellationToken).Bounded();

        // Act
        var act = async () => await sut.ClickAsync(Chat, "e99999", TestContext.Current.CancellationToken).Bounded();

        // Assert
        await act.Should().ThrowAsync<BrowserException>().WithMessage("*browser_snapshot*");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task ClickAsync_NotARef_Throws()
    {
        // Arrange
        using var sut = Create();

        // Act
        var act = async () => await sut.ClickAsync(Chat, "button", TestContext.Current.CancellationToken).Bounded();

        // Assert
        await act.Should().ThrowAsync<BrowserException>().WithMessage("*not an element ref*");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task ScreenshotAsync_Page_ReturnsPng()
    {
        // Arrange
        using var sut = Create();
        await sut.NavigateAsync(Chat, Url("/"), TestContext.Current.CancellationToken).Bounded();

        // Act
        var png = await sut.ScreenshotAsync(Chat, fullPage: true, TestContext.Current.CancellationToken).Bounded();

        // Assert
        png.AsSpan(0, 4).ToArray().Should().Equal(0x89, 0x50, 0x4E, 0x47);
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task DownloadAsync_FileLink_SavesTheFileAndLeavesNoTemporaryFolder()
    {
        // Arrange
        using var sut = Create();
        var page = await sut.NavigateAsync(Chat, Url("/form"), TestContext.Current.CancellationToken).Bounded();
        var destination = Path.Combine(_folder.Root, "out", "saved.txt");

        // Act
        var download = await sut.DownloadAsync(Chat, RefOf(page, "link \"Get file\""), destination, TestContext.Current.CancellationToken).Bounded();

        // Assert
        download.FileName.Should().Be("report.txt");
        download.Bytes.Should().Be(5);
        (await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken)).Should().Be("hello");
        Directory.GetDirectories(Path.Combine(_folder.Root, "downloads")).Should().BeEmpty();
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task DownloadAsync_ElementOffersNoFile_ExplainsNothingWasDownloaded()
    {
        // Arrange
        using var sut = Create(time: TimeProvider.System, downloadTimeout: TimeSpan.FromSeconds(1));
        var page = await sut.NavigateAsync(Chat, Url("/form"), TestContext.Current.CancellationToken).Bounded();

        // Act
        var act = async () => await sut.DownloadAsync(Chat, RefOf(page, "button \"Greet\""), Path.Combine(_folder.Root, "x.txt"), TestContext.Current.CancellationToken).Bounded();

        // Assert
        await act.Should().ThrowAsync<BrowserException>().WithMessage("*Nothing was downloaded*");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task UserClickAndKey_FieldAndButton_ReachThePage()
    {
        // Arrange
        using var sut = Create();
        await sut.NavigateAsync(Chat, Url("/click"), TestContext.Current.CancellationToken).Bounded();

        // Act
        await sut.UserClickAsync(Chat, 0.5, 0.5, TestContext.Current.CancellationToken).Bounded();
        await sut.UserKeyAsync(Chat, "A", TestContext.Current.CancellationToken).Bounded();
        await sut.UserKeyAsync(Chat, "Enter", TestContext.Current.CancellationToken).Bounded();
        var view = await sut.SnapshotAsync(Chat, TestContext.Current.CancellationToken).Bounded();

        // Assert
        view.Snapshot.Should().Contain("clicked").And.Contain("typed A").And.Contain("enter");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task UserMoveAsync_OverElement_ShowsItsHoverEffect()
    {
        // Arrange
        using var sut = Create();
        await sut.NavigateAsync(Chat, Url("/hover"), TestContext.Current.CancellationToken).Bounded();

        // Act
        await sut.UserMoveAsync(Chat, 0.5, 0.5, TestContext.Current.CancellationToken).Bounded();
        var view = await sut.SnapshotAsync(Chat, TestContext.Current.CancellationToken).Bounded();

        // Assert
        view.Snapshot.Should().Contain("hovered");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task ScrollAsync_DownAndUp_MovesThePageByAScreen()
    {
        // Arrange
        using var sut = Create();
        await sut.NavigateAsync(Chat, Url("/scroll"), TestContext.Current.CancellationToken).Bounded();

        // Act
        var down = await sut.ScrollAsync(Chat, BrowserScrollDirection.Down, TestContext.Current.CancellationToken).Bounded();
        var up = await sut.ScrollAsync(Chat, BrowserScrollDirection.Up, TestContext.Current.CancellationToken).Bounded();

        // Assert
        down.Snapshot.Should().Contain("scrolled 640");
        up.Snapshot.Should().Contain("scrolled 0");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task ScrollAsync_BottomThenTop_ReachesBothEnds()
    {
        // Arrange
        using var sut = Create();
        await sut.NavigateAsync(Chat, Url("/scroll"), TestContext.Current.CancellationToken).Bounded();

        // Act
        var bottom = await sut.ScrollAsync(Chat, BrowserScrollDirection.Bottom, TestContext.Current.CancellationToken).Bounded();
        var top = await sut.ScrollAsync(Chat, BrowserScrollDirection.Top, TestContext.Current.CancellationToken).Bounded();

        // Assert
        bottom.Snapshot.Should().MatchRegex("scrolled [1-9]\\d{3}");
        top.Snapshot.Should().Contain("scrolled 0");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task ScrollAsync_NothingOpened_Throws()
    {
        // Arrange
        using var sut = Create();

        // Act
        var act = async () => await sut.ScrollAsync(Chat, BrowserScrollDirection.Down, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<BrowserException>().WithMessage("*browser_navigate*");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task WaitAsync_TextAppearsLater_ReturnsThePageWithIt()
    {
        // Arrange
        using var sut = Create(time: TimeProvider.System);
        await sut.NavigateAsync(Chat, Url("/delayed"), TestContext.Current.CancellationToken).Bounded();

        // Act
        var view = await sut.WaitAsync(Chat, TimeSpan.FromSeconds(10), "Finally ready", TestContext.Current.CancellationToken).Bounded();

        // Assert
        view.Snapshot.Should().Contain("Finally ready");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task WaitAsync_TextNeverAppears_ReturnsThePageAfterTheLimit()
    {
        // Arrange
        using var sut = Create(time: TimeProvider.System);
        await sut.NavigateAsync(Chat, Url("/"), TestContext.Current.CancellationToken).Bounded();

        // Act
        var view = await sut.WaitAsync(Chat, TimeSpan.FromMilliseconds(300), "never shown", TestContext.Current.CancellationToken).Bounded();

        // Assert
        view.Title.Should().Be("Demo page");
        view.Snapshot.Should().NotContain("never shown");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task WaitAsync_NoText_WaitsThenReadsThePage()
    {
        // Arrange
        using var sut = Create(time: TimeProvider.System);
        await sut.NavigateAsync(Chat, Url("/delayed"), TestContext.Current.CancellationToken).Bounded();

        // Act
        var view = await sut.WaitAsync(Chat, TimeSpan.FromSeconds(1), null, TestContext.Current.CancellationToken).Bounded();

        // Assert
        view.Snapshot.Should().Contain("Finally ready");
    }

    [Fact(Skip = Reason, SkipUnless = nameof(TestChrome.IsAvailable), SkipType = typeof(TestChrome))]
    public async Task WaitAsync_NothingOpened_Throws()
    {
        // Arrange
        using var sut = Create(time: TimeProvider.System);

        // Act
        var act = async () => await sut.WaitAsync(Chat, TimeSpan.FromMilliseconds(100), null, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<BrowserException>().WithMessage("*browser_navigate*");
    }

    private static string RefOf(BrowserPageView view, string line) =>
        Regex.Match(view.Snapshot, Regex.Escape(line) + @" .*?\[ref=(e\d+)\]").Groups[1].Value;

    private static string Html(string title, string body) => $"<!doctype html><html><head><title>{title}</title></head><body>{body}</body></html>";

    private PuppeteerBrowserEngine Create(string? executablePath = "default", int? appPort = null, TimeProvider? time = null, TimeSpan? downloadTimeout = null) =>
        new(
            appPort,
            _folder.Root,
            new LoggerFake<PuppeteerBrowserEngine>(),
            time ?? new FakeTimeProvider(),
            executablePath: executablePath == "default" ? TestChrome.Path : executablePath,
            downloadTimeout: downloadTimeout);

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
                case "/form":
                    body = Html(
                        "Form",
                        "<label for=\"n\">Name</label><input id=\"n\">" +
                        "<label for=\"c\">Color</label><select id=\"c\"><option value=\"r\">Red</option><option value=\"g\">Green</option></select>" +
                        "<button onclick=\"document.getElementById('out').textContent = 'Hello ' + document.getElementById('n').value + ' ' + document.getElementById('c').value\">Greet</button>" +
                        "<p id=\"out\">nothing yet</p><a href=\"/file\">Get file</a>");
                    break;
                case "/click":
                    body = Html(
                        "Click",
                        "<style>body{margin:0}#target{width:1280px;height:800px;display:block}</style>" +
                        "<div id=\"log\"></div><button id=\"target\" onclick=\"log.append('clicked ')\">Target</button>" +
                        "<script>document.addEventListener('keydown', event => log.append(event.key === 'Enter' ? 'enter ' : 'typed ' + event.key + ' '));</script>");
                    break;
                case "/hover":
                    body = Html(
                        "Hover",
                        "<style>body{margin:0}#target{width:1280px;height:800px}</style>" +
                        "<div id=\"target\">Target</div><p id=\"out\">waiting</p>" +
                        "<script>target.addEventListener('mouseover', () => out.textContent = 'hovered');</script>");
                    break;
                case "/scroll":
                    body = Html(
                        "Scroll",
                        "<style>body{margin:0}#y{position:fixed;top:0}#tall{height:5000px}</style>" +
                        "<div id=\"y\">scrolled 0</div><div id=\"tall\"></div>" +
                        "<script>addEventListener('scroll', () => y.textContent = 'scrolled ' + Math.round(scrollY));</script>");
                    break;
                case "/delayed":
                    body = Html("Delayed", "<div id=\"out\">loading</div><script>setTimeout(() => out.textContent = 'Finally ready', 400);</script>");
                    break;
                case "/file":
                    response.ContentType = "application/octet-stream";
                    response.AppendHeader("Content-Disposition", "attachment; filename=\"report.txt\"");
                    body = "hello";
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
