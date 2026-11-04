#pragma warning disable IDISP001, IDISP003 // The engine owns the browser and sweeper; Dispose and CloseBrowserAsync release them.
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using System.Collections.Concurrent;
using System.Text.Json;

namespace BlazeHarness.Core.Web.Browser;

/// <summary>
/// Runs headless Chrome through PuppeteerSharp, with one browser context per chat so chats share no cookies or storage.
/// </summary>
/// <remarks>
/// Every request a page makes passes an interceptor that applies <see cref="BrowserAddressPolicy"/>, so redirects, scripts and frames
/// can't reach beyond localhost. Chrome itself is also pointed at a proxy that refuses everything, which loopback addresses bypass,
/// so workers and anything else the interceptor doesn't see fail too. A page may only navigate its top frame to an address that was
/// opened with <see cref="NavigateAsync"/>, which the caller has had approved. One lock serializes everything, which is enough for a
/// single local user.
/// </remarks>
internal sealed partial class PuppeteerBrowserEngine : IBrowserEngine
{
    internal const int MaxSnapshotChars = 30_000;

    private const string MissingBrowserMessage = "The browser isn't installed yet. Tell the user to open Settings > Web and choose Install browser.";

    private const int ViewportWidth = 1280;
    private const int ViewportHeight = 800;
    private const int ScreencastQuality = 85;
    private const int ScrollStep = ViewportHeight * 4 / 5;
    private const int ScrollToEnd = 1_000_000;

    private static readonly HashSet<string> UserKeys = new(StringComparer.Ordinal)
    {
        "Enter", "Backspace", "Delete", "Tab", "Escape", "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Home", "End", "PageUp", "PageDown",
    };

    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);
    // Chrome sends the next screencast frame only after the last one is acknowledged, so waiting before the
    // acknowledgement caps the frame rate (about 30 per second) without dropping the frame that shows where the page ended up.
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(33);
    // Reading the title is a round trip to the page, so a frame of the same page reuses a title read this recently.
    private static readonly TimeSpan TitleMaxAge = TimeSpan.FromSeconds(1);

    private readonly int? _appPort;
    private readonly BrowserFetcher _fetcher;
    private readonly string _downloadRoot;
    private readonly string? _executablePath;
    private readonly SemaphoreSlim _install = new(1, 1);
    private readonly TimeSpan _idleLimit;
    private readonly TimeSpan _downloadTimeout;
    private readonly TimeProvider _time;
    private readonly ILogger<PuppeteerBrowserEngine> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, BrowserSession> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, BrowserFrame> _frames = new(StringComparer.Ordinal);
    private IBrowser? _browser;
    private ITimer? _sweeper;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PuppeteerBrowserEngine"/> class.
    /// </summary>
    /// <param name="appPort">The port BlazeHarness listens on, which pages may not open, or null when it is not known.</param>
    /// <param name="browserDirectory">The folder the browser is downloaded to.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="time">The clock.</param>
    /// <param name="idleLimit">How long a chat's page stays open without being used; ten minutes by default.</param>
    /// <param name="executablePath">A Chrome program to use instead of the downloaded one, or null.</param>
    /// <param name="downloadTimeout">How long a download may take once it was started; a minute by default.</param>
    public PuppeteerBrowserEngine(
        int? appPort,
        string browserDirectory,
        ILogger<PuppeteerBrowserEngine> logger,
        TimeProvider time,
        TimeSpan? idleLimit = null,
        string? executablePath = null,
        TimeSpan? downloadTimeout = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(browserDirectory);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(time);
        _appPort = appPort;
        _fetcher = new BrowserFetcher(new BrowserFetcherOptions { Browser = SupportedBrowser.ChromeHeadlessShell, Path = browserDirectory });
        _downloadRoot = Path.Combine(browserDirectory, "downloads");
        _executablePath = executablePath;
        _logger = logger;
        _time = time;
        _idleLimit = idleLimit ?? TimeSpan.FromMinutes(10);
        _downloadTimeout = downloadTimeout ?? TimeSpan.FromMinutes(1);
    }

    /// <summary>
    /// Gets how many chats have a page open.
    /// </summary>
    internal int OpenPages => _sessions.Count;

    /// <inheritdoc />
    public event EventHandler<BrowserFrameChangedEventArgs>? FrameChanged;

    /// <inheritdoc />
    public BrowserFrame? LatestFrame(string chatId)
    {
        ArgumentException.ThrowIfNullOrEmpty(chatId);
        return _frames.GetValueOrDefault(chatId);
    }

    /// <inheritdoc />
    public Task<bool> IsInstalledAsync(CancellationToken cancellationToken) => Task.FromResult(FindExecutable() is not null);

    /// <inheritdoc />
    public async Task<bool> InstallAsync(CancellationToken cancellationToken)
    {
        await _install.WaitAsync(cancellationToken);
        try
        {
            await _fetcher.DownloadAsync();
            return FindExecutable() is not null;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or PuppeteerException)
        {
            LogInstallFailed(ex);
            return false;
        }
        finally
        {
            _install.Release();
        }
    }

    /// <inheritdoc />
    public async Task<BrowserPageView> NavigateAsync(string chatId, Uri url, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(chatId);
        ArgumentNullException.ThrowIfNull(url);
        if (BrowserAddressPolicy.Refusal(url, _appPort) is { } refusal)
        {
            throw new BrowserException(refusal);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var session = await SessionAsync(chatId, cancellationToken);
            session.Touch(_time.GetUtcNow());
            session.Approve(BrowserAddressPolicy.OriginOf(url));
            session.TakeProblems();
            IResponse? response;
            try
            {
                response = await session.Page
                    .GoToAsync(url.AbsoluteUri, new NavigationOptions { Timeout = (int)NavigationTimeout.TotalMilliseconds, WaitUntil = [WaitUntilNavigation.Load] })
                    .WaitAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is PuppeteerException or TimeoutException)
            {
                throw new BrowserException(OpenFailure(ex, url));
            }

            await SettleAsync(session.Page, cancellationToken);
            return await ViewAsync(session, response is null ? null : (int)response.Status, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<BrowserPageView> SnapshotAsync(string chatId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(chatId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_sessions.TryGetValue(chatId, out var session) || session.Page.IsClosed || session.Page.Url == "about:blank")
            {
                throw new BrowserException("No page is open. Call browser_navigate first.");
            }

            session.Touch(_time.GetUtcNow());
            return await ViewAsync(session, null, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public Task<BrowserPageView> ClickAsync(string chatId, string elementRef, CancellationToken cancellationToken)
    {
        var backendNodeId = ParseRef(elementRef);
        return WithPageAsync(
            chatId,
            async session =>
            {
                var (x, y) = await BrowserElementActions.LocateAsync(session.Client, backendNodeId, cancellationToken);
                session.TakeProblems();
                await session.Page.Mouse.ClickAsync(x, y);
                await SettleAsync(session.Page, cancellationToken);
                return await ViewAsync(session, null, cancellationToken);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<BrowserPageView> TypeAsync(string chatId, string elementRef, string text, bool submit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        var backendNodeId = ParseRef(elementRef);
        return WithPageAsync(
            chatId,
            async session =>
            {
                session.TakeProblems();
                if (await BrowserElementActions.PrepareTypingAsync(session.Client, backendNodeId, text, cancellationToken))
                {
                    if (text.Length == 0)
                    {
                        await session.Page.Keyboard.PressAsync("Backspace");
                    }
                    else
                    {
                        await session.Page.Keyboard.SendCharacterAsync(text);
                    }
                }

                if (submit)
                {
                    await session.Page.Keyboard.PressAsync("Enter");
                }

                await SettleAsync(session.Page, cancellationToken);
                return await ViewAsync(session, null, cancellationToken);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<BrowserPageView> ScrollAsync(string chatId, BrowserScrollDirection direction, CancellationToken cancellationToken) =>
        WithPageAsync(
            chatId,
            async session =>
            {
                session.TakeProblems();
                await session.Page.Mouse.MoveAsync(ViewportX(0.5), ViewportY(0.5));
                await session.Page.Mouse.WheelAsync(0, direction switch
                {
                    BrowserScrollDirection.Down => ScrollStep,
                    BrowserScrollDirection.Up => -ScrollStep,
                    BrowserScrollDirection.Top => -ScrollToEnd,
                    BrowserScrollDirection.Bottom => ScrollToEnd,
                    _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
                });
                await SettleAsync(session.Page, cancellationToken);
                return await ViewAsync(session, null, cancellationToken);
            },
            cancellationToken);

    /// <inheritdoc />
    public async Task<BrowserPageView> WaitAsync(string chatId, TimeSpan duration, string? text, CancellationToken cancellationToken)
    {
        // Waiting happens outside the lock so a long wait doesn't hold up the user's clicks or other chats.
        var page = await WithPageAsync(chatId, session => Task.FromResult(session.Page), cancellationToken);
        try
        {
            if (string.IsNullOrEmpty(text))
            {
                await Task.Delay(duration, _time, cancellationToken);
            }
            else
            {
                await page
                    .WaitForFunctionAsync("wanted => document.body !== null && document.body.innerText.includes(wanted)", new WaitForFunctionOptions { Timeout = (int)duration.TotalMilliseconds }, text)
                    .WaitAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is PuppeteerException or TimeoutException)
        {
            // The text did not show up in time, or the page went away; the view below shows what there is, or says the page is gone.
        }

        return await SnapshotAsync(chatId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<byte[]> ScreenshotAsync(string chatId, bool fullPage, CancellationToken cancellationToken) =>
        WithPageAsync(
            chatId,
            session => session.Page.ScreenshotDataAsync(new ScreenshotOptions { Type = ScreenshotType.Png, FullPage = fullPage }).WaitAsync(cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<BrowserDownload> DownloadAsync(string chatId, string elementRef, string destination, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(destination);
        var backendNodeId = ParseRef(elementRef);
        return WithPageAsync(
            chatId,
            async session =>
            {
                var folder = Path.Combine(_downloadRoot, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                try
                {
                    var (x, y) = await BrowserElementActions.LocateAsync(session.Client, backendNodeId, cancellationToken);
                    var (fileName, saved) = await BrowserDownloader.RunAsync(
                        session.BrowserClient,
                        session.Context.Id,
                        folder,
                        () => session.Page.Mouse.ClickAsync(x, y),
                        _time,
                        _downloadTimeout,
                        cancellationToken);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? ".");
                    File.Move(saved, destination, overwrite: true);
                    return new BrowserDownload(fileName, new FileInfo(destination).Length);
                }
                finally
                {
                    Directory.Delete(folder, recursive: true);
                }
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task UserClickAsync(string chatId, double x, double y, CancellationToken cancellationToken) =>
        WithPageAsync(chatId, session => session.Page.Mouse.ClickAsync(ViewportX(x), ViewportY(y)), cancellationToken);

    /// <inheritdoc />
    public Task UserMoveAsync(string chatId, double x, double y, CancellationToken cancellationToken) =>
        WithPageAsync(chatId, session => session.Page.Mouse.MoveAsync(ViewportX(x), ViewportY(y)), cancellationToken);

    /// <inheritdoc />
    public Task UserKeyAsync(string chatId, string key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length == 1)
        {
            return WithPageAsync(chatId, session => session.Page.Keyboard.TypeAsync(key), cancellationToken);
        }

        return UserKeys.Contains(key)
            ? WithPageAsync(chatId, session => session.Page.Keyboard.PressAsync(key), cancellationToken)
            : Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UserScrollAsync(string chatId, double x, double y, double deltaY, CancellationToken cancellationToken) =>
        WithPageAsync(
            chatId,
            async session =>
            {
                await session.Page.Mouse.MoveAsync(ViewportX(x), ViewportY(y));
                await session.Page.Mouse.WheelAsync(0, (decimal)deltaY);
            },
            cancellationToken);

    /// <inheritdoc />
    public void Forget(string chatId)
    {
        ArgumentException.ThrowIfNullOrEmpty(chatId);
        _ = ForgetAsync(chatId);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sweeper?.Dispose();
        _browser?.Dispose();
        _gate.Dispose();
        _install.Dispose();
    }

    private static string OpenFailure(Exception ex, Uri url)
    {
        var origin = BrowserAddressPolicy.OriginOf(url);
        if (ex is TimeoutException or WaitTaskTimeoutException)
        {
            return $"{origin} did not finish loading within {NavigationTimeout.TotalSeconds:0} seconds.";
        }

        var message = ex.Message;
        var start = message.IndexOf("net::", StringComparison.Ordinal);
        if (start < 0)
        {
            return $"Could not open {origin}.";
        }

        var code = message[(start + "net::".Length)..].Split(' ', 2)[0];
        return code == "ERR_BLOCKED_BY_CLIENT"
            ? "The page led somewhere the browser may not open. Only localhost pages that were opened with browser_navigate are loaded."
            : $"Could not open {origin} ({code}). Check that the service is running and listening on that port. A server a sandboxed shell command starts is only reachable here when it was started with the shell tool's background option.";
    }

    private static async Task SettleAsync(IPage page, CancellationToken cancellationToken)
    {
        try
        {
            await page.WaitForNetworkIdleAsync(new WaitForNetworkIdleOptions { Timeout = (int)SettleTimeout.TotalMilliseconds }).WaitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is PuppeteerException or TimeoutException)
        {
            // A page that keeps polling never goes idle; what has rendered by now is what gets read.
        }
    }

    private static async Task<BrowserPageView> ViewAsync(BrowserSession session, int? status, CancellationToken cancellationToken)
    {
        var page = session.Page;
        string title;
        string snapshot;
        try
        {
            title = await page.GetTitleAsync().WaitAsync(cancellationToken);
            var tree = await session.Client.SendAsync<JsonElement>("Accessibility.getFullAXTree").WaitAsync(cancellationToken);
            snapshot = AccessibilityTextRenderer.Render(tree.TryGetProperty("nodes", out var nodes) ? nodes : default);
        }
        catch (PuppeteerException)
        {
            throw new BrowserException("The page could not be read; it may have navigated away. Call browser_navigate again.");
        }

        var clipped = snapshot.Length > MaxSnapshotChars;
        return new BrowserPageView(new Uri(page.Url), title, status, clipped ? snapshot[..MaxSnapshotChars] : snapshot, clipped, session.TakeProblems());
    }

    private static long ParseRef(string elementRef) =>
        BrowserElementRef.TryParse(elementRef, out var id)
            ? id
            : throw new BrowserException("That is not an element ref. Use a ref such as e42 from the last browser_snapshot.");

    private static decimal ViewportX(double fraction) => (decimal)(Math.Clamp(fraction, 0, 1) * (ViewportWidth - 1));

    private static decimal ViewportY(double fraction) => (decimal)(Math.Clamp(fraction, 0, 1) * (ViewportHeight - 1));

    private static string WithoutQuery(string url) => url.Split('?', 2)[0];

    private async Task<T> WithPageAsync<T>(string chatId, Func<BrowserSession, Task<T>> action, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(chatId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_sessions.TryGetValue(chatId, out var session) || session.Page.IsClosed || session.Page.Url == "about:blank")
            {
                throw new BrowserException("No page is open. Call browser_navigate first.");
            }

            session.Touch(_time.GetUtcNow());
            try
            {
                return await action(session);
            }
            catch (Exception ex) when (ex is PuppeteerException or TimeoutException)
            {
                LogActionFailed(ex);
                throw new BrowserException("The page could not do that; it may have changed or navigated away. Call browser_snapshot to see it as it is now.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task WithPageAsync(string chatId, Func<BrowserSession, Task> action, CancellationToken cancellationToken) =>
        WithPageAsync(
            chatId,
            async session =>
            {
                await action(session);
                return true;
            },
            cancellationToken);

    private string? FindExecutable()
    {
        if (_executablePath is not null)
        {
            return File.Exists(_executablePath) ? _executablePath : null;
        }

        var path = _fetcher.GetInstalledBrowsers().Select(browser => browser.GetExecutablePath()).FirstOrDefault();
        return path is not null && File.Exists(path) ? path : null;
    }

    private async Task<BrowserSession> SessionAsync(string chatId, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(chatId, out var existing))
        {
            if (!existing.Page.IsClosed)
            {
                return existing;
            }

            await CloseSessionAsync(chatId);
        }

        var browser = await BrowserAsync(cancellationToken);
        var context = await browser.CreateBrowserContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportAsync(new ViewPortOptions { Width = ViewportWidth, Height = ViewportHeight });
        var client = await page.CreateCDPSessionAsync();
        var browserClient = await browser.CreateCDPSessionAsync();
        await BrowserDownloader.DenyAsync(browserClient, context.Id);
        var session = new BrowserSession(context, page, client, browserClient, _time.GetUtcNow());
        await page.SetRequestInterceptionAsync(true);
        page.Request += (_, args) => OnRequestInBackground(session, args.Request);
        page.Console += (_, args) =>
        {
            // Chrome logs failed loads as a URL-less console error; the Response handler already reports them with the URL.
            if (args.Message.Type == ConsoleType.Error && !args.Message.Text.StartsWith("Failed to load resource", StringComparison.Ordinal))
            {
                session.AddProblem("console error: " + args.Message.Text);
            }
        };
        page.PageError += (_, args) => session.AddProblem("script error: " + args.Message);
        page.RequestFailed += (_, args) =>
        {
            if (args.Request.FailureText is { } failure && !failure.Contains("BLOCKED_BY_CLIENT", StringComparison.Ordinal))
            {
                session.AddProblem($"request failed: {args.Request.Method} {WithoutQuery(args.Request.Url)} ({failure})");
            }
        };
        page.Response += (_, args) =>
        {
            if ((int)args.Response.Status >= 400 && !args.Response.Url.EndsWith("/favicon.ico", StringComparison.Ordinal))
            {
                session.AddProblem($"{(int)args.Response.Status} {args.Response.Request.Method} {WithoutQuery(args.Response.Url)}");
            }
        };
        _sessions[chatId] = session;
        await StartScreencastAsync(chatId, session);
        return session;
    }

    private async Task StartScreencastAsync(string chatId, BrowserSession session)
    {
        try
        {
            var client = session.Client;
            client.MessageReceived += (_, args) =>
            {
                if (args.MessageID == "Page.screencastFrame")
                {
                    OnFrameInBackground(chatId, session, client, args.MessageData);
                }
            };
            await client.SendAsync("Page.startScreencast", new { format = "jpeg", quality = ScreencastQuality, maxWidth = ViewportWidth, maxHeight = ViewportHeight });
        }
        catch (PuppeteerException ex)
        {
            // The agent can still read the page; only the view next to the chat stays empty.
            LogScreencastFailed(ex);
        }
    }

    private void OnFrameInBackground(string chatId, BrowserSession session, ICDPSession client, JsonElement data) => _ = OnFrameAsync(chatId, session, client, data);

    private async Task OnFrameAsync(string chatId, BrowserSession session, ICDPSession client, JsonElement data)
    {
        try
        {
            var jpeg = Convert.FromBase64String(data.GetProperty("data").GetString() ?? string.Empty);
            var ackId = data.GetProperty("sessionId").GetInt32();
            if (Uri.TryCreate(session.Page.Url, UriKind.Absolute, out var url) && url.Scheme != "about" && !session.IsClosed)
            {
                var frame = new BrowserFrame(jpeg, url, await TitleAsync(session, url));
                _frames[chatId] = frame;
                FrameChanged?.Invoke(this, new BrowserFrameChangedEventArgs(chatId, frame));
            }

            await Task.Delay(FrameInterval, _time);
            await client.SendAsync("Page.screencastFrameAck", new { sessionId = ackId });
        }
        catch (Exception ex) when (ex is PuppeteerException or FormatException or JsonException or KeyNotFoundException or InvalidOperationException or ObjectDisposedException)
        {
            // The page closed or navigated while the frame was handled; the next frame replaces this one.
        }
    }

    private async Task<string> TitleAsync(BrowserSession session, Uri url)
    {
        var now = _time.GetUtcNow();
        if (session.FreshTitle(url, now, TitleMaxAge) is { } fresh)
        {
            return fresh;
        }

        var title = await session.Page.GetTitleAsync();
        session.RememberTitle(url, title, now);
        return title;
    }

    private void PublishClosed(string chatId)
    {
        if (_frames.TryRemove(chatId, out _))
        {
            FrameChanged?.Invoke(this, new BrowserFrameChangedEventArgs(chatId, null));
        }
    }

    private void OnRequestInBackground(BrowserSession session, IRequest request) => _ = OnRequestAsync(session, request);

    private async Task OnRequestAsync(BrowserSession session, IRequest request)
    {
        var refusal = RefusalFor(session, request);
        try
        {
            if (refusal is null)
            {
                await request.ContinueAsync();
            }
            else
            {
                session.AddProblem(refusal);
                await request.AbortAsync(RequestAbortErrorCode.BlockedByClient);
            }
        }
        catch (PuppeteerException)
        {
            // The page closed while the request was in flight, so there is nothing left to answer.
        }
    }

    private string? RefusalFor(BrowserSession session, IRequest request)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
        {
            return "Blocked a request to an address that could not be read.";
        }

        var origin = BrowserAddressPolicy.OriginOf(uri);
        if (BrowserAddressPolicy.Refusal(uri, _appPort) is not null)
        {
            return $"Blocked a request to {origin}: the browser only loads localhost pages, and not BlazeHarness itself.";
        }

        return request.IsNavigationRequest && request.Frame?.ParentFrame is null && !session.IsApproved(origin)
            ? $"Blocked a navigation to {origin}: open it with browser_navigate first."
            : null;
    }

    private async Task<IBrowser> BrowserAsync(CancellationToken cancellationToken)
    {
        if (_browser is { IsConnected: true } running)
        {
            return running;
        }

        foreach (var (chatId, stale) in _sessions.ToList())
        {
            stale.MarkClosed();
            PublishClosed(chatId);
        }

        _sessions.Clear();
        var executable = FindExecutable() ?? throw new BrowserException(MissingBrowserMessage);
        try
        {
            _browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Browser = SupportedBrowser.ChromeHeadlessShell,
                ExecutablePath = executable,
                Headless = true,
                HeadlessMode = HeadlessMode.Shell,
                AcceptInsecureCerts = true,
                DefaultViewport = null,
                Args = ["--proxy-server=http://127.0.0.1:1"],
            }).WaitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is PuppeteerException or IOException)
        {
            LogLaunchFailed(ex);
            throw new BrowserException("The browser could not be started. On Linux the system libraries it needs may be missing; see the app log.");
        }

        _sweeper ??= _time.CreateTimer(_ => SweepInBackground(), null, SweepInterval, SweepInterval);
        return _browser;
    }

    private async Task ForgetAsync(string chatId)
    {
        try
        {
            await _gate.WaitAsync();
            try
            {
                await CloseSessionAsync(chatId);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception ex) when (ex is PuppeteerException or ObjectDisposedException)
        {
            LogCloseFailed(ex);
        }
    }

    private void SweepInBackground() => _ = SweepAsync();

    private async Task SweepAsync()
    {
        try
        {
            await _gate.WaitAsync();
            try
            {
                var cutoff = _time.GetUtcNow() - _idleLimit;
                foreach (var id in _sessions.Where(pair => pair.Value.LastUsed < cutoff).Select(pair => pair.Key).ToList())
                {
                    await CloseSessionAsync(id);
                }

                if (_sessions.Count == 0)
                {
                    await CloseBrowserAsync();
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception ex) when (ex is PuppeteerException or ObjectDisposedException)
        {
            LogCloseFailed(ex);
        }
    }

    private async Task CloseSessionAsync(string chatId)
    {
        if (!_sessions.Remove(chatId, out var session))
        {
            return;
        }

        session.MarkClosed();
        PublishClosed(chatId);
        try
        {
            await session.Context.CloseAsync();
        }
        catch (PuppeteerException)
        {
            // Closing an already closed context is the same outcome as closing it.
        }
    }

    private async Task CloseBrowserAsync()
    {
        _sweeper?.Dispose();
        _sweeper = null;
        if (_browser is not null)
        {
            await _browser.CloseAsync();
            _browser = null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The browser could not be installed.")]
    private partial void LogInstallFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The browser could not be launched.")]
    private partial void LogLaunchFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The browser view could not be started.")]
    private partial void LogScreencastFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A browser action failed.")]
    private partial void LogActionFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Closing a browser page failed.")]
    private partial void LogCloseFailed(Exception ex);
}
