namespace BlazeHarness.Core.Web.Search;

/// <summary>
/// Merges ranked lists from several providers into one, with reciprocal rank fusion.
/// </summary>
internal static class RankFusion
{
    private const double RankOffset = 60;

    /// <summary>
    /// Merge ranked lists and remove duplicate pages.
    /// </summary>
    /// <param name="lists">Each provider's id and its results, best first, in the order the providers are tried.</param>
    /// <param name="maxResults">The most results to return.</param>
    /// <returns>The merged results, best first.</returns>
    public static IReadOnlyList<MergedResult> Merge(IReadOnlyList<(string Provider, IReadOnlyList<SearchResult> Results)> lists, int maxResults)
    {
        Dictionary<string, Entry> byUrl = new(StringComparer.Ordinal);
        List<Entry> order = [];
        foreach (var (provider, results) in lists)
        {
            for (var rank = 0; rank < results.Count; rank++)
            {
                var result = results[rank];
                var key = UrlCanonicalizer.Canonical(result.Url);
                if (!byUrl.TryGetValue(key, out var entry))
                {
                    entry = new Entry(result);
                    byUrl[key] = entry;
                    order.Add(entry);
                }
                else if (result.Snippet.Length > entry.Result.Snippet.Length)
                {
                    entry.Result = entry.Result with { Snippet = result.Snippet };
                }

                if (!entry.Providers.Contains(provider))
                {
                    entry.Providers.Add(provider);
                    entry.Score += 1 / (RankOffset + rank + 1);
                }
            }
        }

        return [.. order.OrderByDescending(entry => entry.Score).Take(maxResults).Select(entry => new MergedResult(entry.Result, entry.Providers))];
    }

    private sealed class Entry(SearchResult result)
    {
        public SearchResult Result { get; set; } = result;

        public List<string> Providers { get; } = [];

        public double Score { get; set; }
    }
}
