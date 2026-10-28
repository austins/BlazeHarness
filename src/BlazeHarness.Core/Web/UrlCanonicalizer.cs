using System.Text;

namespace BlazeHarness.Core.Web;

/// <summary>
/// Reduces web addresses to a form that is the same for links to the same page, for removing duplicates and caching.
/// </summary>
internal static class UrlCanonicalizer
{
    private static readonly string[] TrackingParameters = ["fbclid", "gclid", "msclkid", "mc_cid", "mc_eid", "igshid", "ref", "ref_src"];

    /// <summary>
    /// Get the canonical form of an address.
    /// </summary>
    /// <param name="uri">An absolute http or https address.</param>
    /// <returns>The form without the fragment, a leading <c>www.</c>, a default port, tracking parameters or a trailing slash.</returns>
    public static string Canonical(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var host = uri.IdnHost;
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host[4..];
        }

        var builder = new StringBuilder();
        builder.Append(uri.Scheme).Append("://").Append(host);
        if (!uri.IsDefaultPort)
        {
            builder.Append(':').Append(uri.Port);
        }

        var path = uri.AbsolutePath;
        builder.Append(path.Length > 1 ? path.TrimEnd('/') : string.Empty);
        var kept = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair => !IsTracking(pair.Split('=', 2)[0]))
            .ToList();
        if (kept.Count > 0)
        {
            builder.Append('?').AppendJoin('&', kept);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Get the canonical form of address text, or the text itself when it is not an absolute address.
    /// </summary>
    /// <param name="url">The address text.</param>
    /// <returns>The canonical form.</returns>
    public static string Canonical(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? Canonical(uri) : url;
    }

    private static bool IsTracking(string name)
    {
        return name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) || Array.Exists(TrackingParameters, p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
    }
}
