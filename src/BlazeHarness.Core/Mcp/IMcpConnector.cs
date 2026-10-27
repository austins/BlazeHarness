namespace BlazeHarness.Core.Mcp;

/// <summary>
/// Starts or reaches an MCP server and connects to it.
/// </summary>
public interface IMcpConnector
{
    /// <summary>
    /// Connect to a server.
    /// </summary>
    /// <param name="config">The server's configuration with every variable already expanded.</param>
    /// <param name="cancellationToken">A token to cancel the attempt.</param>
    /// <returns>The connection.</returns>
    /// <exception cref="McpConnectException">The server could not be started or reached.</exception>
    Task<IMcpConnection> ConnectAsync(McpServerConfig config, CancellationToken cancellationToken);
}
