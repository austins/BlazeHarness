namespace BlazeHarness.Core.Mcp;

/// <summary>
/// Where a connection to an MCP server stands.
/// </summary>
public enum McpServerState
{
    /// <summary>
    /// The server is not running; it starts the first time a chat that has it turned on needs its tools.
    /// </summary>
    Stopped,

    /// <summary>
    /// The server is starting.
    /// </summary>
    Connecting,

    /// <summary>
    /// The server is running and its tools are known.
    /// </summary>
    Connected,

    /// <summary>
    /// The server could not be reached, or it stopped.
    /// </summary>
    Failed,
}
