namespace BlazeHarness.Core.Web.Search.Providers;

/// <summary>
/// Searches with Exa's public MCP endpoint, which needs no key.
/// </summary>
internal sealed class ExaSearchProvider : ISearchProvider
{
    internal const string Endpoint = "https://mcp.exa.ai/mcp";

    private static readonly string[] Labels = ["Title:", "URL:", "Highlights:", "Published date:", "Author:", "Score:"];

    private readonly HttpClient _client;
    private readonly Uri _endpoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExaSearchProvider"/> class.
    /// </summary>
    /// <param name="client">The client to send requests with.</param>
    /// <param name="endpoint">The endpoint, or null for the public one.</param>
    public ExaSearchProvider(HttpClient client, Uri? endpoint = null)
    {
        _client = client;
        _endpoint = endpoint ?? new Uri(Endpoint);
    }

    /// <inheritdoc />
    public string Id => WebProviders.Exa;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        var body = McpSearchResponse.CallBody("web_search_exa", new { query, numResults = maxResults, type = "auto", livecrawl = "fallback", contextMaxCharacters = 3000 });
        var reply = await SearchHttp.PostJsonAsync(_client, _endpoint, body, acceptEventStream: true, cancellationToken);
        List<SearchResult> results = [];
        foreach (var text in McpSearchResponse.TextBlocks(reply))
        {
            foreach (var block in text.Split("\n---\n"))
            {
                if (results.Count >= maxResults)
                {
                    return results;
                }

                if (Parse(block) is { } result)
                {
                    results.Add(result);
                }
            }
        }

        return results;
    }

    private static SearchResult? Parse(string block)
    {
        string title = string.Empty;
        string url = string.Empty;
        string snippet = string.Empty;
        foreach (var raw in block.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("Title:", StringComparison.Ordinal))
            {
                title = line[6..].Trim();
            }
            else if (line.StartsWith("URL:", StringComparison.Ordinal))
            {
                url = line[4..].Trim();
            }
            else if (snippet.Length == 0 && line.Length > 0 && !Array.Exists(Labels, label => line.StartsWith(label, StringComparison.Ordinal)))
            {
                snippet = line;
            }
        }

        return title.Length > 0 && SnippetText.IsWebAddress(url)
            ? new SearchResult(SnippetText.OneLine(title, 200), url, SnippetText.OneLine(snippet, 300))
            : null;
    }
}
