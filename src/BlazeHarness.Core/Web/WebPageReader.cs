using BlazeHarness.Core.Web.Content;

namespace BlazeHarness.Core.Web;

/// <summary>
/// Downloads an address and turns it into readable text, using the session's cache.
/// </summary>
internal sealed class WebPageReader
{
    private readonly SafeWebClient _client;
    private readonly ContentExtractor _extractor;
    private readonly WebCache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebPageReader"/> class.
    /// </summary>
    /// <param name="client">Downloads addresses.</param>
    /// <param name="extractor">Reads the downloaded content.</param>
    /// <param name="cache">Keeps pages for the session.</param>
    public WebPageReader(SafeWebClient client, ContentExtractor extractor, WebCache cache)
    {
        _client = client;
        _extractor = extractor;
        _cache = cache;
    }

    /// <summary>
    /// Read an address.
    /// </summary>
    /// <param name="sessionId">The session the call belongs to.</param>
    /// <param name="address">The address.</param>
    /// <param name="cancellationToken">A token that stops the download.</param>
    /// <returns>The page.</returns>
    /// <exception cref="WebFetchException">The address could not be read; the message says why and can be shown to the model.</exception>
    public async Task<CachedPage> ReadAsync(string sessionId, Uri address, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (_cache.Find(sessionId, address) is { } cached)
        {
            return cached;
        }

        var response = await _client.FetchAsync(address, cancellationToken);
        if (response.RedirectTo is { } target)
        {
            throw new WebFetchException($"The address redirects to a different site, {target.AbsoluteUri}. Fetch that address if you want to follow it.");
        }

        if (response.BotChallenge)
        {
            throw new WebFetchException("The site asked for a human check (a bot challenge), which this tool can't pass.");
        }

        if (response.Status is < 200 or >= 300)
        {
            throw new WebFetchException($"The site answered with HTTP status {response.Status}.");
        }

        var page = new CachedPage(response.FinalUri, _extractor.Extract(response, cancellationToken));
        _cache.Add(sessionId, address, page);
        return page;
    }
}
