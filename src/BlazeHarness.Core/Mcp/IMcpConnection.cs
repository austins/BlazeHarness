using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Mcp;

/// <summary>
/// A live connection to one MCP server.
/// </summary>
public interface IMcpConnection : IAsyncDisposable
{
    /// <summary>
    /// Gets a task that completes when the connection has ended, whether the server exited or the link dropped.
    /// </summary>
    Task Closed { get; }

    /// <summary>
    /// List the tools the server offers.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The tools.</returns>
    Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Call a tool.
    /// </summary>
    /// <param name="name">The tool's own name on its server.</param>
    /// <param name="argumentsJson">The arguments as a JSON object.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The tool's output as text; an error result when the server reported one.</returns>
    Task<ToolResult> CallToolAsync(string name, string argumentsJson, CancellationToken cancellationToken);
}
