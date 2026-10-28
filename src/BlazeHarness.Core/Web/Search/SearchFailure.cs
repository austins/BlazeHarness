namespace BlazeHarness.Core.Web.Search;

/// <summary>
/// Why a search provider gave no results.
/// </summary>
public enum SearchFailure
{
    /// <summary>
    /// The provider said to slow down.
    /// </summary>
    RateLimited,

    /// <summary>
    /// The provider refused the request, for example with a bot check.
    /// </summary>
    Blocked,

    /// <summary>
    /// The provider could not be reached or had a server error.
    /// </summary>
    Unavailable,

    /// <summary>
    /// The provider answered with something this version can't read.
    /// </summary>
    BadResponse,
}
