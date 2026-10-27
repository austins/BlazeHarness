using System.Text.RegularExpressions;

namespace BlazeHarness.Core.Mcp;

/// <summary>
/// Finds secrets written straight into a server's settings, where <c>${NAME}</c> would keep them out of <c>mcp.json</c>.
/// </summary>
public static partial class McpSecrets
{
    /// <summary>
    /// List the settings of a server that hold a value that looks like a secret and is not a <c>${NAME}</c> reference.
    /// </summary>
    /// <param name="server">The server as written in the file.</param>
    /// <returns>A label for each such setting, such as <c>env GITHUB_TOKEN</c>, <c>header Authorization</c> or <c>url</c>, with a note when it goes over plain http to another machine. The values are never included.</returns>
    public static IReadOnlyList<string> LiteralsIn(McpServerConfig server)
    {
        ArgumentNullException.ThrowIfNull(server);

        var clearText = IsClearText(server.Url);
        var suffix = clearText ? " (sent over plain http)" : string.Empty;
        List<string> found = [];
        found.AddRange(server.Env.Where(pair => LooksSecret(pair.Key, pair.Value)).Select(pair => "env " + pair.Key));
        found.AddRange(server.Headers.Where(pair => LooksSecret(pair.Key, pair.Value)).Select(pair => "header " + pair.Key + suffix));
        if (server.Url is { } url && !url.Contains("${", StringComparison.Ordinal) && SecretInUrl().IsMatch(url))
        {
            found.Add("url" + suffix);
        }

        return found;
    }

    private static bool IsClearText(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback;
    }

    [GeneratedRegex(@"[?&](api[_-]?key|key|token|access_token|secret|password)=[^&]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SecretInUrl();

    [GeneratedRegex(@"key|token|secret|passw|credential|auth|bearer", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SecretName();

    [GeneratedRegex(@"^(Bearer\s+\S+|sk-\S+|ghp_\S+|github_pat_\S+|xox[baprs]-\S+|AKIA[0-9A-Z]{12,})", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SecretValue();

    private static bool LooksSecret(string name, string value)
    {
        return value.Length > 0
            && !value.Contains("${", StringComparison.Ordinal)
            && (SecretName().IsMatch(name) || SecretValue().IsMatch(value));
    }
}
