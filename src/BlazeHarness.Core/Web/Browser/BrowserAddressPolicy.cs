using System.Globalization;
using System.Net;

namespace BlazeHarness.Core.Web.Browser;

/// <summary>
/// Decides which addresses the built-in browser may load: pages on this computer, except BlazeHarness itself.
/// </summary>
/// <remarks>
/// This is the reverse of <see cref="WebAddressPolicy"/>, which keeps the fetch tool on the public internet. The browser runs scripts,
/// so it is limited to the services a developer starts locally. BlazeHarness is refused because a page it showed could be same-origin
/// with the app and click its approval buttons.
/// </remarks>
internal static class BrowserAddressPolicy
{
    /// <summary>
    /// Check an address.
    /// </summary>
    /// <param name="uri">The address of a page or of something a page loads.</param>
    /// <param name="appPort">The port BlazeHarness listens on, or null when it is not known.</param>
    /// <returns>Null when the browser may load it, otherwise what to tell the model.</returns>
    public static string? Refusal(Uri uri, int? appPort)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Scheme is not ("http" or "https"))
        {
            return "Only http and https addresses can be opened.";
        }

        if (!IsLoopbackHost(uri))
        {
            return "The browser only opens pages on this computer (localhost, 127.0.0.1 or [::1]). Use web_fetch for the internet.";
        }

        return appPort == uri.Port ? "That port is BlazeHarness itself, which the browser may not open." : null;
    }

    /// <summary>
    /// Get the host and port an address belongs to, the way approvals are remembered.
    /// </summary>
    /// <param name="uri">The address.</param>
    /// <returns>The lower-case host and port, such as <c>localhost:3000</c>.</returns>
    public static string OriginOf(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var host = WebUrl.HostOf(uri);
        return (host.Contains(':', StringComparison.Ordinal) ? "[" + host + "]" : host) + ":" + uri.Port.ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsLoopbackHost(Uri uri)
    {
        var host = WebUrl.HostOf(uri);
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(uri.DnsSafeHost, out var address) && IPAddress.IsLoopback(address);
    }
}
