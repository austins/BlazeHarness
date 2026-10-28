namespace BlazeHarness.Core.Web;

/// <summary>
/// How the enabled search providers are used for one search.
/// </summary>
public enum WebSearchMode
{
    /// <summary>
    /// Every enabled provider is asked at once and the results are merged, so a slow or failing provider costs nothing.
    /// </summary>
    Parallel,

    /// <summary>
    /// The providers are tried in the saved order, and the next one is asked only when the one before failed or found nothing.
    /// </summary>
    Sequential,
}
