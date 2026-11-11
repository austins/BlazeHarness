namespace BlazeHarness.Core.Tools.Web;

/// <summary>
/// One result read back from the output of a <c>web_search</c> call.
/// </summary>
/// <param name="Title">The title of the page.</param>
/// <param name="Url">The address of the page, always http or https.</param>
/// <param name="Snippet">A short extract of the page, or an empty string.</param>
public sealed record WebSearchHit(string Title, Uri Url, string Snippet);
