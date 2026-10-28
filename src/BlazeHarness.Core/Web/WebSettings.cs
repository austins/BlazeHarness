namespace BlazeHarness.Core.Web;

/// <summary>
/// The web tools' switches and search providers, saved in <c>config.json</c>.
/// </summary>
public sealed record WebSettings
{
    /// <summary>
    /// Gets a value indicating whether the model gets the <c>web_search</c> tool.
    /// </summary>
    public bool SearchEnabled { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the model gets the <c>web_fetch</c> tool.
    /// </summary>
    public bool FetchEnabled { get; init; } = true;

    /// <summary>
    /// Gets how the enabled providers are used for one search.
    /// </summary>
    public WebSearchMode SearchMode { get; init; } = WebSearchMode.Parallel;

    /// <summary>
    /// Gets the saved provider order and switches, or null to use every known provider in its default order.
    /// </summary>
    public IReadOnlyList<WebProviderSetting>? Providers { get; init; }

    /// <summary>
    /// Get every known provider once, in the saved order, with providers added by a newer version at the end.
    /// </summary>
    /// <returns>The providers; unknown and repeated ids in the saved list are dropped.</returns>
    public IReadOnlyList<WebProviderSetting> ResolveProviders()
    {
        List<WebProviderSetting> result = [];
        var known = WebProviders.All.Select(info => info.Id).ToList();
        result.AddRange((Providers ?? []).Where(saved => known.Contains(saved.Id)).DistinctBy(saved => saved.Id, StringComparer.Ordinal));
        result.AddRange(known.Where(id => result.TrueForAll(item => item.Id != id)).Select(id => new WebProviderSetting(id)));
        return result;
    }
}
