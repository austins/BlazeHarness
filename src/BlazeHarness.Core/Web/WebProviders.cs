namespace BlazeHarness.Core.Web;

/// <summary>
/// The search providers the app knows, none of which needs an API key.
/// </summary>
public static class WebProviders
{
    /// <summary>
    /// The id of DuckDuckGo.
    /// </summary>
    public const string DuckDuckGo = "ddg";

    /// <summary>
    /// The id of Exa.
    /// </summary>
    public const string Exa = "exa";

    /// <summary>
    /// The id of Parallel.
    /// </summary>
    public const string Parallel = "parallel";

    /// <summary>
    /// The id of Keenable.
    /// </summary>
    public const string Keenable = "keenable";

    /// <summary>
    /// Gets the providers in their default order.
    /// </summary>
    public static IReadOnlyList<WebProviderInfo> All { get; } =
    [
        new(DuckDuckGo, "DuckDuckGo", "Reads the HTML results page. No account; the site may limit heavy use."),
        new(Exa, "Exa", "The public Exa search endpoint, with no account."),
        new(Parallel, "Parallel", "The public Parallel search endpoint, with no account."),
        new(Keenable, "Keenable", "The public Keenable search endpoint, which limits requests per hour."),
    ];

    /// <summary>
    /// Get a provider's display name.
    /// </summary>
    /// <param name="id">The provider id.</param>
    /// <returns>The name, or the id when the provider is unknown.</returns>
    public static string NameOf(string id)
    {
        return All.FirstOrDefault(info => string.Equals(info.Id, id, StringComparison.Ordinal))?.Name ?? id;
    }
}
