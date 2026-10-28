namespace BlazeHarness.Core.Web.Search.Providers;

/// <summary>
/// A web search service the <c>web_search</c> tool can ask.
/// </summary>
internal interface ISearchProvider
{
    /// <summary>
    /// Gets the id saved in the settings, one of <see cref="WebProviders.All"/>.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Search the web.
    /// </summary>
    /// <param name="query">The search text.</param>
    /// <param name="maxResults">The most results to return.</param>
    /// <param name="cancellationToken">A token that stops the search.</param>
    /// <returns>The results in the provider's order; empty when nothing matched.</returns>
    /// <exception cref="SearchProviderException">The provider could not be used.</exception>
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken);
}
