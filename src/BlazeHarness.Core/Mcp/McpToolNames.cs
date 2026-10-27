using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BlazeHarness.Core.Mcp;

/// <summary>
/// Builds the names the model sees for MCP tools, which put the server first so two servers can offer a tool with the same name.
/// </summary>
/// <remarks>
/// A name is <c>server__tool</c> and uses only letters, digits, <c>_</c> and <c>-</c>, up to <see cref="MaxLength"/> characters, which is what model APIs accept.
/// Server names have no <c>__</c> and do not end in <c>_</c>, so the first <c>__</c> always ends the server name.
/// </remarks>
public static partial class McpToolNames
{
    /// <summary>
    /// The text between the server name and the tool name.
    /// </summary>
    public const string Separator = "__";

    /// <summary>
    /// The longest name model APIs accept.
    /// </summary>
    public const int MaxLength = 64;

    /// <summary>
    /// The longest server name; it leaves room in <see cref="MaxLength"/> for the separator and a shortened tool name.
    /// </summary>
    public const int MaxServerLength = 32;

    private const int HashLength = 6;

    private const int HashSuffixLength = HashLength + 1;

    /// <summary>
    /// Split a name the model used into its server and tool parts.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <param name="server">The server name.</param>
    /// <param name="tool">The rest of the name; it may be shortened when the original was too long.</param>
    /// <returns>True when the name has a server part; built-in tools do not.</returns>
    public static bool TrySplit(string name, out string server, out string tool)
    {
        ArgumentNullException.ThrowIfNull(name);
        var at = name.IndexOf(Separator, StringComparison.Ordinal);
        if (at <= 0 || at + Separator.Length >= name.Length)
        {
            server = string.Empty;
            tool = name;
            return false;
        }

        server = name[..at];
        tool = name[(at + Separator.Length)..];
        return true;
    }

    internal static string Build(string server, string tool, ISet<string> taken)
    {
        ArgumentException.ThrowIfNullOrEmpty(server);
        ArgumentException.ThrowIfNullOrEmpty(tool);
        ArgumentNullException.ThrowIfNull(taken);

        var cleaned = Unsafe().Replace(tool, "_");
        var full = server + Separator + cleaned;

        // A name that was rewritten gets a hash of the original so it does not depend on which tool came first.
        string name;
        if (full.Length <= MaxLength && cleaned == tool)
        {
            name = full;
        }
        else
        {
            var kept = full[..Math.Min(full.Length, MaxLength - HashSuffixLength)];
            name = kept + "_" + Hash(server + Separator + tool);
        }

        var candidate = name;
        var copy = 1;
        while (!taken.Add(candidate))
        {
            copy++;
            var suffix = "_" + copy.ToString(System.Globalization.CultureInfo.InvariantCulture);
            candidate = name[..Math.Min(name.Length, MaxLength - suffix.Length)] + suffix;
        }

        return candidate;
    }

    private static string Hash(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..HashLength];
    }

    [GeneratedRegex("[^A-Za-z0-9_-]", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Unsafe();
}
