using BlazeHarness.Core.Tools;
using BlazeHarness.Core.Web.Content;
using BlazeHarness.Core.Web.Search;
using BlazeHarness.Core.Web.Search.Providers;
using Microsoft.Extensions.Logging;

namespace BlazeHarness.Core.Web;

/// <summary>
/// Owns the web search and fetch machinery and hands each session its web tools.
/// </summary>
public sealed class WebServices : IDisposable
{
    private readonly Func<WebSettings> _settings;
    private readonly SearchOrchestrator _search;
    private readonly SafeWebClient _client;
    private readonly WebPageReader _reader;
    private readonly WebCache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebServices"/> class.
    /// </summary>
    /// <param name="settings">Reads the current web settings; it is called on every turn, so changes apply at once.</param>
    /// <param name="searchClient">The HTTP client the search providers use.</param>
    /// <param name="loggers">Creates the logger.</param>
    /// <param name="time">The clock.</param>
    public WebServices(Func<WebSettings> settings, HttpClient searchClient, ILoggerFactory loggers, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(searchClient);
        ArgumentNullException.ThrowIfNull(loggers);
        ArgumentNullException.ThrowIfNull(time);
        _settings = settings;
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
        _reader = new WebPageReader(_client, new ContentExtractor(new PdfPigTextExtractor()), _cache);
    }

    /// <summary>
    /// Get how a search provider did the last time it was used.
    /// </summary>
    /// <param name="providerId">The provider id.</param>
    /// <returns>The status, or null when it has not been used yet.</returns>
    public WebProviderStatus? StatusOf(string providerId) => _search.StatusOf(providerId);

    /// <summary>
    /// Drop the pages a session fetched.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    public void Forget(string sessionId) => _cache.Forget(sessionId);

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();

    /// <summary>
    /// Get the web tools a session can use right now.
    /// </summary>
    /// <param name="sessionId">The session id, which scopes the page cache.</param>
    /// <returns>The tools that are turned on in the settings.</returns>
    internal IReadOnlyList<ITool> ToolsFor(string sessionId)
    {
        var settings = _settings();
        List<ITool> tools = [];
        if (settings.SearchEnabled)
        {
            tools.Add(new WebSearchTool(_search, _settings));
        }

        if (settings.FetchEnabled)
        {
            tools.Add(new WebFetchTool(_reader, sessionId));
        }

        return tools;
    }
}
