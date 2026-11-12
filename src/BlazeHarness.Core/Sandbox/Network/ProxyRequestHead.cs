using System.Globalization;
using System.Text;

namespace BlazeHarness.Core.Sandbox.Network;

/// <summary>
/// The first lines of a request a sandboxed command sends to the proxy, read far enough to tell where it wants to go.
/// </summary>
/// <param name="Method">The HTTP method, upper case.</param>
/// <param name="Host">The host the request is for, without brackets or a port.</param>
/// <param name="Port">The port the request is for.</param>
/// <param name="IsTunnel">True for <c>CONNECT</c>, which opens a tunnel; false for a plain request to forward.</param>
/// <param name="Forward">For a plain request, the request as the origin server should get it. Empty for a tunnel.</param>
internal sealed record ProxyRequestHead(string Method, string Host, int Port, bool IsTunnel, byte[] Forward)
{
    private const int DefaultHttpPort = 80;

    /// <summary>
    /// Read the head of a request.
    /// </summary>
    /// <param name="head">The request line and the header lines, ending before the blank line.</param>
    /// <returns>The parsed head, or null when the proxy can't serve it.</returns>
    public static ProxyRequestHead? TryParse(string head)
    {
        ArgumentNullException.ThrowIfNull(head);
        var lines = head.Split("\r\n");
        var parts = lines[0].Split(' ');
        if (parts.Length != 3 || !parts[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
        {
            return null;
        }

        var method = parts[0].ToUpperInvariant();
        return method == "CONNECT" ? ParseTunnel(method, parts[1]) : ParseForward(method, parts[1], parts[2], lines);
    }

    private static ProxyRequestHead? ParseTunnel(string method, string authority)
    {
        string host;
        string port;
        if (authority.StartsWith('[', StringComparison.Ordinal))
        {
            var close = authority.IndexOf("]:", StringComparison.Ordinal);
            if (close < 0)
            {
                return null;
            }

            host = authority[1..close];
            port = authority[(close + 2)..];
        }
        else
        {
            var colon = authority.LastIndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                return null;
            }

            host = authority[..colon];
            port = authority[(colon + 1)..];
        }

        return IsValid(host) && int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is > 0 and <= 65535
            ? new ProxyRequestHead(method, Normalize(host), number, IsTunnel: true, [])
            : null;
    }

    private static ProxyRequestHead? ParseForward(string method, string target, string version, string[] lines)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp || !IsValid(uri.DnsSafeHost))
        {
            return null;
        }

        var host = Normalize(uri.DnsSafeHost);
        var port = uri.Port > 0 ? uri.Port : DefaultHttpPort;
        var stripped = StrippedHeaders(lines);
        var forward = new StringBuilder();
        forward.Append(method).Append(' ').Append(uri.PathAndQuery).Append(' ').Append(version).Append("\r\n");
        forward.Append("Host: ").Append(FormatAuthority(host, port)).Append("\r\n");
        foreach (var line in lines.Skip(1))
        {
            var name = line.Split(':', 2)[0].Trim();
            if (name.Length == 0 || stripped.Contains(name))
            {
                continue;
            }

            forward.Append(line).Append("\r\n");
        }

        // One request per connection, so what comes after it can never be sent to another host than the one that was checked.
        forward.Append("Connection: close\r\n\r\n");
        return new ProxyRequestHead(method, host, port, IsTunnel: false, Encoding.Latin1.GetBytes(forward.ToString()));
    }

    private static HashSet<string> StrippedHeaders(string[] lines)
    {
        HashSet<string> stripped = [with(StringComparer.OrdinalIgnoreCase), "Host", "Proxy-Connection", "Proxy-Authorization", "Connection", "Keep-Alive", "Upgrade"];
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split(':', 2);
            if (parts.Length == 2 && parts[0].Trim().Equals("Connection", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var token in parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    stripped.Add(token);
                }
            }
        }

        return stripped;
    }

    private static string FormatAuthority(string host, int port)
    {
        var bracketed = host.Contains(':', StringComparison.Ordinal) ? "[" + host + "]" : host;
        return port == DefaultHttpPort ? bracketed : string.Create(CultureInfo.InvariantCulture, $"{bracketed}:{port}");
    }

    private static bool IsValid(string host) => host.Length is > 0 and <= 253 && Uri.CheckHostName(host) != UriHostNameType.Unknown;

#pragma warning disable CA1308 // Host names are compared in lower case.
    private static string Normalize(string host) => host.TrimEnd('.').ToLowerInvariant();
#pragma warning restore CA1308
}
