using BlazeHarness.Core.Tools;
using BlazeHarness.Core.Tools.Web;
using BlazeHarness.Core.Web.Browser;
using BlazeHarness.Core.Web.Content;
using BlazeHarness.Core.Web.Search;
using BlazeHarness.Core.Web.Search.Providers;
using Microsoft.Extensions.Logging;

namespace BlazeHarness.Core.Web;

/// <summary>
/// Owns the web search and fetch machinery and hands each chat its web tools.
/// </summary>
public sealed class WebServices : IDisposable
{
    private readonly Func<WebSettings> _settings;
    private readonly SearchOrchestrator _search;
    private readonly SafeWebClient _client;
    private readonly WebPageReader _reader;
    private readonly WebCache _cache;
#pragma warning disable IDISP008 // The engine is always handed over for this class to own: the public constructor creates it and tests pass a fake.
    private readonly IBrowserEngine _browser;
#pragma warning restore IDISP008
    private readonly int? _appPort;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebServices"/> class.
    /// </summary>
    /// <param name="settings">Reads the current web settings; it is called on every turn, so changes apply at once.</param>
    /// <param name="searchClient">The HTTP client the search providers use.</param>
    /// <param name="loggers">Creates the logger.</param>
    /// <param name="time">The clock.</param>
    /// <param name="browserDirectory">The folder the browser the browser tools use is downloaded to.</param>
    /// <param name="appPort">The port BlazeHarness listens on, which the browser tools refuse to open, or null when it is not known.</param>
    public WebServices(Func<WebSettings> settings, HttpClient searchClient, ILoggerFactory loggers, TimeProvider time, string browserDirectory, int? appPort = null)
        : this(settings, searchClient, loggers, time, appPort, CreateBrowser(loggers, time, browserDirectory, appPort))
    {
    }

    internal WebServices(Func<WebSettings> settings, HttpClient searchClient, ILoggerFactory loggers, TimeProvider time, int? appPort, IBrowserEngine browser)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(searchClient);
        ArgumentNullException.ThrowIfNull(loggers);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(browser);
        _settings = settings;
        _browser = browser;
        _appPort = appPort;
        _search = new SearchOrchestrator(
            [
                new DuckDuckGoSearchProvider(searchClient),
                new ExaSearchProvider(searchClient),
                new ParallelSearchProvider(searchClient),
                new KeenableSearchProvider(searchClient),
            ],
            time,
            loggers.CreateLogger<SearchOrchestrator>());
        _client = new SafeWebClient();
        _cache = new WebCache(time);
        _reader = new WebPageReader(_client, new ContentExtractor(new PdfPigTextExtractor(), new OpenXmlTextExtractor()), _cache);
    }

    /// <summary>
    /// Occurs when a chat's browser shows something new, and with no frame when its page is closed. It may be raised on any thread.
    /// </summary>
    public event EventHandler<BrowserFrameChangedEventArgs>? BrowserFrameChanged
    {
        add => _browser.FrameChanged += value;
        remove => _browser.FrameChanged -= value;
    }

    /// <summary>
    /// Get how a search provider did the last time it was used.
    /// </summary>
    /// <param name="providerId">The provider id.</param>
    /// <returns>The status, or null when it has not been used yet.</returns>
    public WebProviderStatus? StatusOf(string providerId) => _search.StatusOf(providerId);

    /// <summary>
    /// Get the last picture of a chat's browser page.
    /// </summary>
    /// <param name="chatId">The chat id.</param>
    /// <returns>The picture, or null when the chat has no page open.</returns>
    public BrowserFrame? LatestBrowserFrame(string chatId) => _browser.LatestFrame(chatId);

    /// <summary>
    /// Drop the pages a chat fetched.
    /// </summary>
    /// <param name="chatId">The chat id.</param>
    public void Forget(string chatId)
    {
        _cache.Forget(chatId);
        _browser.Forget(chatId);
    }

    /// <summary>
    /// Tell whether the browser behind the <c>browser_*</c> tools is installed.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the check.</param>
    /// <returns>True when pages can be opened.</returns>
    public Task<bool> IsBrowserInstalledAsync(CancellationToken cancellationToken) => _browser.IsInstalledAsync(cancellationToken);

    /// <summary>
    /// Download the browser behind the <c>browser_*</c> tools (headless Chrome), about 125 MB.
    /// </summary>
    /// <param name="cancellationToken">A token that stops waiting for the download.</param>
    /// <returns>True when the browser is installed afterwards.</returns>
    public Task<bool> InstallBrowserAsync(CancellationToken cancellationToken) => _browser.InstallAsync(cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        _client.Dispose();
        _browser.Dispose();
    }

    /// <summary>
    /// Get the web tools a chat can use right now.
    /// </summary>
    /// <param name="chatId">The chat id, which scopes the page cache.</param>
    /// <returns>The tools that are turned on in the settings.</returns>
    internal IReadOnlyList<ITool> ToolsFor(string chatId)
    {
        var settings = _settings();
        List<ITool> tools = [];
        if (settings.SearchEnabled)
        {
            tools.Add(new WebSearchTool(_search, _settings));
        }

        if (settings.FetchEnabled)
        {
            tools.Add(new WebFetchTool(_reader, chatId));
        }

        if (settings.BrowserEnabled)
        {
            tools.Add(new BrowserNavigateTool(_browser, chatId, _appPort));
            tools.Add(new BrowserSnapshotTool(_browser, chatId));
            tools.Add(new BrowserClickTool(_browser, chatId));
            tools.Add(new BrowserTypeTool(_browser, chatId));
            tools.Add(new BrowserScreenshotTool(_browser, chatId));
            tools.Add(new BrowserDownloadTool(_browser, chatId));
        }

        return tools;
    }

    private static PuppeteerBrowserEngine CreateBrowser(ILoggerFactory loggers, TimeProvider time, string browserDirectory, int? appPort)
    {
        ArgumentNullException.ThrowIfNull(loggers);
        return new PuppeteerBrowserEngine(appPort, browserDirectory, loggers.CreateLogger<PuppeteerBrowserEngine>(), time);
    }
}
