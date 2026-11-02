using System.IO.Enumeration;

namespace BlazeHarness.Core.Mcp.Tools;

/// <summary>
/// Matches tool names against the patterns in <c>mcp.json</c>.
/// </summary>
internal static class McpToolPatterns
{
    public static bool Matches(IReadOnlyList<string> patterns, string tool)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(tool);
        return patterns.Any(pattern => pattern == "*" || FileSystemName.MatchesSimpleExpression(pattern, tool, ignoreCase: false));
    }
}
