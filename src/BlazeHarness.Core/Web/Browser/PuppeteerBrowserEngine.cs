#pragma warning disable IDISP001, IDISP003 // The engine owns the browser and sweeper; Dispose and CloseBrowserAsync release them.
using Microsoft.Extensions.Logging;
using PuppeteerSharp;

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

    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly int? _appPort;
    private readonly BrowserFetcher _fetcher;
    private readonly string? _executablePath;
    private readonly SemaphoreSlim _install = new(1, 1);
    private readonly TimeSpan _idleLimit;
    private readonly TimeProvider _time;
    private readonly ILogger<PuppeteerBrowserEngine> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, BrowserSession> _sessions = new(StringComparer.Ordinal);
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
    public PuppeteerBrowserEngine(
        int? appPort,
        string browserDirectory,
        ILogger<PuppeteerBrowserEngine> logger,
        TimeProvider time,
        TimeSpan? idleLimit = null,
        string? executablePath = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(browserDirectory);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(time);
        _appPort = appPort;
        _fetcher = new BrowserFetcher(new BrowserFetcherOptions { Browser = SupportedBrowser.Chrome, Path = browserDirectory });
        _executablePath = executablePath;
        _logger = logger;
        _time = time;
        _idleLimit = idleLimit ?? TimeSpan.FromMinutes(10);
    }

    /// <summary>
    /// Gets how many chats have a page open.
    /// </summary>
    internal int OpenPages => _sessions.Count;

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
            : $"Could not open {origin} ({code}). Check that the service is running and listening on that port.";
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
            snapshot = AccessibilityTextRenderer.Render(await page.Accessibility.SnapshotAsync().WaitAsync(cancellationToken));
        }
        catch (PuppeteerException)
        {
            throw new BrowserException("The page could not be read; it may have navigated away. Call browser_navigate again.");
        }

        var clipped = snapshot.Length > MaxSnapshotChars;
        return new BrowserPageView(new Uri(page.Url), title, status, clipped ? snapshot[..MaxSnapshotChars] : snapshot, clipped, session.TakeProblems());
    }

    private static string WithoutQuery(string url) => url.Split('?', 2)[0];

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
        await page.SetViewportAsync(new ViewPortOptions { Width = 1280, Height = 800 });
        var session = new BrowserSession(context, page, _time.GetUtcNow());
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
        return session;
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

        _sessions.Clear();
        var executable = FindExecutable() ?? throw new BrowserException(MissingBrowserMessage);
        try
        {
            _browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Browser = SupportedBrowser.Chrome,
                ExecutablePath = executable,
                Headless = true,
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Closing a browser page failed.")]
    private partial void LogCloseFailed(Exception ex);
}
