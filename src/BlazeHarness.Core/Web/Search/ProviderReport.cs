namespace BlazeHarness.Core.Web.Search;

/// <summary>
/// What one provider did for one search.
/// </summary>
/// <param name="Id">The provider id.</param>
/// <param name="Count">How many results it returned.</param>
/// <param name="Failure">Why it returned none, or null when it worked.</param>
/// <param name="Paused">Whether it was skipped because it failed repeatedly just before.</param>
internal sealed record ProviderReport(string Id, int Count, SearchFailure? Failure = null, bool Paused = false);
