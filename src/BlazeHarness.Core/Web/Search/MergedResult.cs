namespace BlazeHarness.Core.Web.Search;

/// <summary>
/// A result with the providers that returned it.
/// </summary>
/// <param name="Result">The result, taking the longest snippet any provider gave.</param>
/// <param name="Providers">The ids of the providers that found it, in the order they are tried.</param>
internal sealed record MergedResult(SearchResult Result, IReadOnlyList<string> Providers);
