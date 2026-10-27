namespace BlazeHarness.Core.Mcp;

/// <summary>
/// How the app talks to an MCP server.
/// </summary>
public enum McpTransport
{
    /// <summary>The app starts a program and talks to it over its standard input and output.</summary>
    Stdio,

    /// <summary>The server is reached with streamable HTTP.</summary>
    Http,

    /// <summary>The server is reached with the older server-sent events transport.</summary>
    Sse,
}
