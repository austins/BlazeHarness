namespace BlazeHarness.Core.Web;

/// <summary>
/// How a search provider did the last time it was used, for the settings page.
/// </summary>
/// <param name="Id">The provider id.</param>
/// <param name="Working">Whether the last search with the provider worked.</param>
/// <param name="Detail">A short sentence on what happened; it never holds a query or a result.</param>
/// <param name="At">When it happened.</param>
public sealed record WebProviderStatus(string Id, bool Working, string Detail, DateTimeOffset At);
