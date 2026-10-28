using HtmlAgilityPack;
using System.Net;

namespace BlazeHarness.Core.Web.Search.Providers;

/// <summary>
/// Searches by reading DuckDuckGo's HTML results page, which needs no key.
/// </summary>
internal sealed class DuckDuckGoSearchProvider : ISearchProvider
{
    internal const string Endpoint = "https://html.duckduckgo.com/html/";

    private const string UserAgent = "Mozilla/5.0 (X11; Linux x86_64; rv:128.0) Gecko/20100101 Firefox/128.0";

    private readonly HttpClient _client;
    private readonly string _endpoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="DuckDuckGoSearchProvider"/> class.
    /// </summary>
    /// <param name="client">The client to send requests with.</param>
    /// <param name="endpoint">The results page address, or null for the public one.</param>
    public DuckDuckGoSearchProvider(HttpClient client, string? endpoint = null)
    {
        _client = client;
        _endpoint = endpoint ?? Endpoint;
    }

    /// <inheritdoc />
    public string Id => WebProviders.DuckDuckGo;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        var page = await SearchHttp.GetAsync(_client, new Uri($"{_endpoint}?q={Uri.EscapeDataString(query)}"), UserAgent, cancellationToken);
        return Parse(page, maxResults);
    }

    internal static IReadOnlyList<SearchResult> Parse(string page, int maxResults)
    {
        var document = new HtmlDocument();
        document.LoadHtml(page);
        List<SearchResult> results = [];
        var nodes = document.DocumentNode.SelectNodes("//*[contains(concat(' ', normalize-space(@class), ' '), ' result ')]");
        foreach (var node in nodes is null ? [] : nodes.AsEnumerable())
        {
            if (results.Count >= maxResults)
            {
                break;
            }

            var link = node.SelectSingleNode(".//*[contains(@class, 'result__title')]//a[contains(@class, 'result__a')]");
            var href = link?.GetAttributeValue("href", string.Empty);
            var title = WebUtility.HtmlDecode(link?.InnerText);
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrEmpty(href) || Unwrap(href) is not { } url)
            {
                continue;
            }

            var snippet = WebUtility.HtmlDecode(node.SelectSingleNode(".//*[contains(@class, 'result__snippet')]")?.InnerText);
            results.Add(new SearchResult(SnippetText.OneLine(title, 200), url, SnippetText.OneLine(snippet, 300)));
        }

        return results;
    }

    // Results link through a redirect page, and the real address is in its uddg parameter.
    private static string? Unwrap(string href)
    {
        var text = href.StartsWith("//", StringComparison.Ordinal) ? "https:" + href : href;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Host.EndsWith("duckduckgo.com", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts[0] == "uddg" && parts.Length == 2)
                {
                    var target = Uri.UnescapeDataString(parts[1].Replace('+', ' '));
                    return SnippetText.IsWebAddress(target) ? target : null;
                }
            }

            return null;
        }

        return SnippetText.IsWebAddress(text) ? text : null;
    }
}
