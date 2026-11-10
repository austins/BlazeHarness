using System.Net;

namespace BlazeHarness.Web.Hosting;

/// <summary>
/// Describes the machine the browser runs on, cascaded to every page.
/// </summary>
/// <remarks>
/// It decides whether to offer actions that happen on the server's desktop, like opening a file in an editor. It is a convenience, not an access check.
/// </remarks>
/// <param name="IsLocal">Whether the browser runs on the machine that runs the app.</param>
internal sealed record ClientMachine(bool IsLocal)
{
    /// <summary>
    /// Tell whether a connection comes from the machine it arrived on.
    /// </summary>
    /// <param name="remote">The address the connection came from.</param>
    /// <param name="local">The address it arrived on.</param>
    /// <returns>True for a loopback address or when both addresses are the same.</returns>
    public static bool IsLocalConnection(IPAddress? remote, IPAddress? local)
    {
        if (remote is null)
        {
            return false;
        }

        remote = remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote;
        local = local is { IsIPv4MappedToIPv6: true } ? local.MapToIPv4() : local;
        return IPAddress.IsLoopback(remote) || remote.Equals(local);
    }
}
