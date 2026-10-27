namespace BlazeHarness.Core.Mcp;

/// <summary>
/// What is known about one configured MCP server.
/// </summary>
/// <param name="Config">The server as written in <c>mcp.json</c>, with its variables not expanded.</param>
/// <param name="State">Where the connection stands.</param>
/// <param name="Error">Why the last attempt failed; null unless <paramref name="State"/> is <see cref="McpServerState.Failed"/>.</param>
/// <param name="Tools">The tools the server offers; empty until it has connected.</param>
public sealed record McpServerStatus(McpServerConfig Config, McpServerState State, string? Error, IReadOnlyList<McpToolInfo> Tools)
{
    /// <summary>
    /// Gets the number of characters the server's tools add to every request, so a user can see the cost of turning it on.
    /// </summary>
    public int PromptCharacters => Tools.Sum(t => t.Name.Length + McpTool.TrimDescription(t.Description).Length + McpTool.NormalizeSchema(t.InputSchema).Length);
}
