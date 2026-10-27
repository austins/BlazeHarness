using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Mcp;

/// <summary>
/// A tool that belongs to an MCP server.
/// </summary>
/// <remarks>
/// Servers run outside the sandbox and the harness cannot see what they touch, so the permission engine treats
/// these tools apart from the built-in ones.
/// </remarks>
public interface IMcpTool : ITool
{
    /// <summary>
    /// Gets the name of the server, as written in <c>mcp.json</c>.
    /// </summary>
    string ServerName { get; }

    /// <summary>
    /// Gets the tool's own name on its server.
    /// </summary>
    string RemoteName { get; }

    /// <summary>
    /// Gets the server's claim that the tool does not change anything, or null when it did not say.
    /// </summary>
    bool? ReadOnlyHint { get; }

    /// <summary>
    /// Gets the server's claim that the tool may destroy data, or null when it did not say.
    /// </summary>
    bool? DestructiveHint { get; }
}
