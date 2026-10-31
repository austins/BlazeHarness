namespace BlazeHarness.Core.Mcp;

/// <summary>
/// The searchable tools of one server.
/// </summary>
/// <param name="Server">The server name.</param>
/// <param name="Tools">The tools that are found with a search, which excludes the ones always offered and the hidden ones.</param>
/// <param name="Error">Why the tools could not be listed, or null.</param>
/// <param name="Known">Whether the tools of the server are known, from a connection or an earlier run.</param>
internal sealed record McpServerListing(string Server, IReadOnlyList<McpToolListing> Tools, string? Error, bool Known);
