namespace BlazeHarness.Core.Mcp;

/// <summary>
/// What was read from an <c>mcp.json</c> text: the servers that are valid, and a message for everything that is not.
/// </summary>
/// <param name="Servers">The valid servers, in file order.</param>
/// <param name="Errors">One message per problem, such as invalid JSON or a server that is missing its command.</param>
public sealed record McpParseResult(IReadOnlyList<McpServerConfig> Servers, IReadOnlyList<string> Errors);
