namespace BlazeHarness.Core.Mcp.Tools;

/// <summary>
/// One tool of an MCP server as a search sees it, without a connection to call it through.
/// </summary>
/// <param name="Name">The name the model calls it by.</param>
/// <param name="Server">The server it belongs to.</param>
/// <param name="RemoteName">The name the server gave it.</param>
/// <param name="Description">The description, trimmed as it would be in a request.</param>
/// <param name="InputSchema">The input schema as JSON text.</param>
internal sealed record McpToolListing(string Name, string Server, string RemoteName, string Description, string InputSchema);
