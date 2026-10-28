using System.Text.Json;

namespace BlazeHarness.Core.Web.Search.Providers;

/// <summary>
/// Searches with Parallel's public MCP endpoint, which needs no key.
/// </summary>
internal sealed class ParallelSearchProvider : ISearchProvider
{
    internal const string Endpoint = "https://search.parallel.ai/mcp";

    private readonly HttpClient _client;
    private readonly Uri _endpoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParallelSearchProvider"/> class.
    /// </summary>
    /// <param name="client">The client to send requests with.</param>
    /// <param name="endpoint">The endpoint, or null for the public one.</param>
    public ParallelSearchProvider(HttpClient client, Uri? endpoint = null)
    {
        _client = client;
        _endpoint = endpoint ?? new Uri(Endpoint);
    }

    /// <inheritdoc />
    public string Id => WebProviders.Parallel;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        var body = McpSearchResponse.CallBody("web_search", new { objective = query, search_queries = new[] { query } });
        var reply = await SearchHttp.PostJsonAsync(_client, _endpoint, body, acceptEventStream: true, cancellationToken);
        List<SearchResult> results = [];
        foreach (var text in McpSearchResponse.TextBlocks(reply))
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                if (!document.RootElement.TryGetProperty("results", out var items) || items.ValueKind != JsonValueKind.Array)
                {
                    throw new SearchProviderException(SearchFailure.BadResponse);
                }

                foreach (var item in JsonItems.Of(items))
                {
                    if (results.Count >= maxResults)
                    {
                        return results;
                    }

                    var title = JsonItems.Text(item, "title");
                    var url = JsonItems.Text(item, "url");
                    if (title.Length > 0 && SnippetText.IsWebAddress(url))
                    {
                        results.Add(new SearchResult(SnippetText.OneLine(title, 200), url, SnippetText.OneLine(FirstExcerpt(item), 300)));
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                throw new SearchProviderException(SearchFailure.BadResponse);
            }
        }

        return results;
    }

    private static string FirstExcerpt(JsonElement item)
    {
        if (item.TryGetProperty("excerpts", out var excerpts))
        {
            foreach (var excerpt in JsonItems.Of(excerpts))
            {
                if (excerpt.ValueKind == JsonValueKind.String && excerpt.GetString() is { } text && !string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return string.Empty;
    }
}
