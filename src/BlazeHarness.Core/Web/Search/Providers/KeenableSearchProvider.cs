using System.Text.Json;

namespace BlazeHarness.Core.Web.Search.Providers;

/// <summary>
/// Searches with Keenable's public endpoint, which needs no key but limits requests per hour.
/// </summary>
internal sealed class KeenableSearchProvider : ISearchProvider
{
    internal const string Endpoint = "https://api.keenable.ai/v1/search/public";

    private readonly HttpClient _client;
    private readonly Uri _endpoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeenableSearchProvider"/> class.
    /// </summary>
    /// <param name="client">The client to send requests with.</param>
    /// <param name="endpoint">The endpoint, or null for the public one.</param>
    public KeenableSearchProvider(HttpClient client, Uri? endpoint = null)
    {
        _client = client;
        _endpoint = endpoint ?? new Uri(Endpoint);
    }

    /// <inheritdoc />
    public string Id => WebProviders.Keenable;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new { query, mode = "pro" });
        var reply = await SearchHttp.PostJsonAsync(_client, _endpoint, body, acceptEventStream: false, cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(reply);
            if (!document.RootElement.TryGetProperty("results", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                throw new SearchProviderException(SearchFailure.BadResponse);
            }

            List<SearchResult> results = [];
            foreach (var item in JsonItems.Of(items))
            {
                if (results.Count >= maxResults)
                {
                    break;
                }

                var title = JsonItems.Text(item, "title");
                var url = JsonItems.Text(item, "url");
                if (title.Length > 0 && SnippetText.IsWebAddress(url))
                {
                    // The snippet is often the whole page text, so it is cut hard.
                    var snippet = JsonItems.Text(item, "description") is { Length: > 0 } description ? description : JsonItems.Text(item, "snippet");
                    results.Add(new SearchResult(SnippetText.OneLine(title, 200), url, SnippetText.OneLine(snippet, 300)));
                }
            }

            return results;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new SearchProviderException(SearchFailure.BadResponse);
        }
    }
}
