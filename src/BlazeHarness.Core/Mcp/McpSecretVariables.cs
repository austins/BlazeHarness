using System.Text;
using System.Text.RegularExpressions;

namespace BlazeHarness.Core.Mcp;

/// <summary>
/// Names the variables that stand for secrets a server's settings keep in the secret store instead of in <c>mcp.json</c>.
/// </summary>
/// <remarks>
/// A secret is written to the file as a whole-value reference such as <c>${MCP_GITHUB_TOKEN}</c>. A server only gets the secrets that its own
/// environment and header entries name this way, so a pasted snippet can't read another server's secret.
/// </remarks>
internal static partial class McpSecretVariables
{
    private const string Prefix = "MCP_";

    public static string NameFor(string server, string key)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(key);
        return Prefix + Clean(server) + "_" + Clean(key);
    }

    public static string StoreKey(string variable)
    {
        ArgumentNullException.ThrowIfNull(variable);
        return "mcp:" + variable;
    }

    public static string Reference(string variable)
    {
        ArgumentNullException.ThrowIfNull(variable);
        return "${" + variable + "}";
    }

    public static bool TryParseReference(string? value, out string variable)
    {
        var match = value is null ? null : WholeReference().Match(value);
        variable = match is { Success: true } ? match.Groups[1].Value : string.Empty;
        return match is { Success: true };
    }

    /// <summary>
    /// Get the secret variables a server's settings use, which are the whole-value references its entries name for themselves.
    /// </summary>
    /// <param name="server">The server's settings.</param>
    public static IReadOnlySet<string> Referenced(McpServerConfig server)
    {
        ArgumentNullException.ThrowIfNull(server);

        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, value) in server.Env.Concat(server.Headers))
        {
            if (TryParseReference(value, out var variable) && string.Equals(variable, NameFor(server.Name, key), StringComparison.Ordinal))
            {
                found.Add(variable);
            }
        }

        return found;
    }

    private static string Clean(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_');
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"^\$\{(MCP_[A-Z0-9_]+)\}$")]
    private static partial Regex WholeReference();
}
