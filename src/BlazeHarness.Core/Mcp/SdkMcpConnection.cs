using BlazeHarness.Core.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace BlazeHarness.Core.Mcp;

/// <summary>
/// An <see cref="IMcpConnection"/> on a client of the official MCP SDK.
/// </summary>
public sealed class SdkMcpConnection : IMcpConnection
{
    private readonly McpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="SdkMcpConnection"/> class.
    /// </summary>
    /// <param name="client">The connected client; the connection owns it and disposes it.</param>
    public SdkMcpConnection(McpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <inheritdoc />
#pragma warning disable VSTHRD003 // The client's completion task is the signal that the link ended.
    public Task Closed => _client.Completion;
#pragma warning restore VSTHRD003

    /// <inheritdoc />
    public async Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(CancellationToken cancellationToken)
    {
        var tools = await _client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return [.. tools.Select(t => new McpToolInfo(
            t.ProtocolTool.Name,
            t.ProtocolTool.Description ?? string.Empty,
            t.ProtocolTool.InputSchema.GetRawText(),
            t.ProtocolTool.Annotations?.ReadOnlyHint,
            t.ProtocolTool.Annotations?.DestructiveHint))];
    }

    /// <inheritdoc />
    public async Task<ToolResult> CallToolAsync(string name, string argumentsJson, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Dictionary<string, JsonElement>? arguments = null;
        if (!string.IsNullOrWhiteSpace(argumentsJson))
        {
            try
            {
                arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson);
            }
            catch (JsonException ex)
            {
                return ToolResult.Error($"The arguments are not a valid JSON object: {ex.Message}");
            }
        }

        try
        {
            var result = await _client.CallToolAsync(new CallToolRequestParams { Name = name, Arguments = arguments }, cancellationToken).ConfigureAwait(false);
            return McpResultText.Convert(result);
        }
        catch (McpException ex)
        {
            return ToolResult.Error(ex.Message);
        }
        catch (IOException ex)
        {
            return ToolResult.Error($"The connection to the server was lost: {ex.Message}");
        }
    }

    /// <inheritdoc />
#pragma warning disable IDISP007 // The connection owns the client it was given.
    public ValueTask DisposeAsync() => _client.DisposeAsync();
#pragma warning restore IDISP007
}
