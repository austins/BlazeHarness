namespace BlazeHarness.Core.Web.Search;

/// <summary>
/// The merged results of one search and what each provider did.
/// </summary>
/// <param name="Results">The results, best first.</param>
/// <param name="Reports">One report for each provider that was asked or skipped.</param>
internal sealed record SearchOutcome(IReadOnlyList<MergedResult> Results, IReadOnlyList<ProviderReport> Reports);
