using System.Globalization;

namespace BlazeHarness.Core.Sandbox.Ports;

/// <summary>
/// Reads which TCP ports the programs in a Linux network namespace listen on, from <c>/proc/&lt;pid&gt;/net/tcp</c> and <c>tcp6</c>.
/// </summary>
/// <remarks>
/// Only ports reachable from the same computer count: those bound to a loopback or to every address.
/// </remarks>
internal static class ListeningPorts
{
    private const string Listen = "0A";
    private const string AnyV4 = "00000000";
    private const string AnyV6 = "00000000000000000000000000000000";
    private const string LoopbackV6 = "00000000000000000000000001000000";
    private const string MappedV4Prefix = "0000000000000000FFFF0000";

    /// <summary>
    /// Read the listening ports of the network namespace a process is in.
    /// </summary>
    /// <param name="pid">The process.</param>
    /// <returns>The ports, or none when the process has ended.</returns>
    public static IReadOnlyList<ListeningPort> Read(int pid)
    {
        var v4 = TryRead(string.Create(CultureInfo.InvariantCulture, $"/proc/{pid}/net/tcp"));
        var v6 = TryRead(string.Create(CultureInfo.InvariantCulture, $"/proc/{pid}/net/tcp6"));
        return Merge(Parse(v4, ipv6: false).Concat(Parse(v6, ipv6: true)));
    }

    /// <summary>
    /// Get the listening ports in one table.
    /// </summary>
    /// <param name="table">The text of a <c>/proc/net/tcp</c> or <c>tcp6</c> file.</param>
    /// <param name="ipv6">True for a <c>tcp6</c> table.</param>
    /// <returns>The ports, as often as they appear.</returns>
    public static IEnumerable<ListeningPort> Parse(string table, bool ipv6)
    {
        foreach (var line in table.Split('\n').Skip(1))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 4 || fields[3] != Listen)
            {
                continue;
            }

            var colon = fields[1].LastIndexOf(':');
            if (colon < 0 || !int.TryParse(fields[1].AsSpan(colon + 1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var port) || port == 0)
            {
                continue;
            }

            if ((ipv6 ? ReachedV6(fields[1][..colon]) : ReachedV4(fields[1][..colon])) is { } overIpv4)
            {
                yield return new ListeningPort(port, overIpv4);
            }
        }
    }

    /// <summary>
    /// Merge the entries of one port.
    /// </summary>
    /// <param name="ports">The entries.</param>
    /// <returns>One entry per port, reached over IPv4 when any entry is.</returns>
    public static IReadOnlyList<ListeningPort> Merge(IEnumerable<ListeningPort> ports)
    {
        return [.. ports.GroupBy(port => port.Port).Select(group => new ListeningPort(group.Key, group.Any(port => port.OverIpv4))).OrderBy(port => port.Port)];
    }

    // The address is the 32-bit value in host order, so 127.0.0.1 reads 0100007F: the first octet is the last pair.
    private static bool? ReachedV4(string address)
    {
        return address == AnyV4 || (address.Length == 8 && address.EndsWith("7F", StringComparison.OrdinalIgnoreCase)) ? true : null;
    }

    private static bool? ReachedV6(string address)
    {
        if (address == AnyV6)
        {
            // A server on every IPv6 address normally takes IPv4 connections too.
            return true;
        }

        if (string.Equals(address, LoopbackV6, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return address.Length == 32 && address.StartsWith(MappedV4Prefix, StringComparison.OrdinalIgnoreCase) ? ReachedV4(address[MappedV4Prefix.Length..]) : null;
    }

    private static string TryRead(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
