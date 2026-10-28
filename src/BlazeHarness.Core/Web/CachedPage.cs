using BlazeHarness.Core.Web.Content;

namespace BlazeHarness.Core.Web;

/// <summary>
/// A fetched page, ready to be shown to the model in pieces.
/// </summary>
/// <param name="Address">The address after same-site redirects.</param>
/// <param name="Document">The readable form.</param>
internal sealed record CachedPage(Uri Address, ExtractedDocument Document);
