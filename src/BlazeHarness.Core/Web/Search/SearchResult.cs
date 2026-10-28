namespace BlazeHarness.Core.Web.Search;

/// <summary>
/// One hit from a search provider.
/// </summary>
/// <param name="Title">The page title, on one line.</param>
/// <param name="Url">The absolute page address.</param>
/// <param name="Snippet">A short excerpt, on one line; empty when the provider gave none.</param>
internal sealed record SearchResult(string Title, string Url, string Snippet);
