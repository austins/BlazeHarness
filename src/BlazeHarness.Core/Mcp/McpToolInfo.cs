namespace BlazeHarness.Core.Mcp;

/// <summary>
/// A tool a server offers, as the server describes it.
/// </summary>
/// <param name="Name">The tool's own name on its server.</param>
/// <param name="Description">The server's description, which may be long or empty.</param>
/// <param name="InputSchema">The JSON schema of the arguments, as JSON text.</param>
/// <param name="ReadOnlyHint">The server's claim that the tool does not change anything, or null when it did not say.</param>
/// <param name="DestructiveHint">The server's claim that the tool may destroy data, or null when it did not say.</param>
public sealed record McpToolInfo(string Name, string Description, string InputSchema, bool? ReadOnlyHint = null, bool? DestructiveHint = null);
