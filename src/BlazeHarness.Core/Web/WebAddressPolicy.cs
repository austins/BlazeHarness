using System.Net;
using System.Net.Sockets;

namespace BlazeHarness.Core.Web;

/// <summary>
/// Decides which network addresses the web tools may connect to: public internet addresses only.
/// </summary>
/// <remarks>
/// Loopback, private, link-local, shared-address, multicast and reserved ranges are refused, so a page or a search result can't point the
/// tools at this computer, the home network or a cloud metadata service.
/// </remarks>
internal static class WebAddressPolicy
{
    /// <summary>
    /// Check an address.
    /// </summary>
    /// <param name="address">The address a name resolved to, or an address written in a URL.</param>
    /// <returns>True when the address is on the public internet.</returns>
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPublicV4(address.GetAddressBytes()),
            AddressFamily.InterNetworkV6 => IsPublicV6(address),
            _ => false,
        };
    }

    private static bool IsPublicV4(byte[] b)
    {
        return !(b[0] is 0 or 10 or 127
            || (b[0] == 100 && b[1] is >= 64 and <= 127)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && b[1] == 0 && b[2] is 0 or 2)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 198 && b[1] is 18 or 19)
            || (b[0] == 198 && b[1] == 51 && b[2] == 100)
            || (b[0] == 203 && b[1] == 0 && b[2] == 113)
            || b[0] >= 224);
    }

    private static bool IsPublicV6(IPAddress address)
    {
        var b = address.GetAddressBytes();
        if (address.Equals(IPAddress.IPv6None) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6Loopback))
        {
            return false;
        }

        // 2002::/16 carries an IPv4 address in the next four bytes, so the embedded address decides.
        if (b[0] == 0x20 && b[1] == 0x02)
        {
            return IsPublicV4([b[2], b[3], b[4], b[5]]);
        }

        var unique = (b[0] & 0xFE) == 0xFC;
        var linkLocal = b[0] == 0xFE && (b[1] & 0xC0) == 0x80;
        var siteLocal = b[0] == 0xFE && (b[1] & 0xC0) == 0xC0;
        var multicast = b[0] == 0xFF;
        var documentation = b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8;
        var teredo = b[0] == 0x20 && b[1] == 0x01 && b[2] == 0 && b[3] == 0;
        var nat64 = b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B;
        return !(unique || linkLocal || siteLocal || multicast || documentation || teredo || nat64 || (b[0] == 0 && b[1] == 0));
    }
}
