using BlazeHarness.Core.Tools;
using System.Text.Json;

namespace BlazeHarness.Core.Mcp.Tools;

/// <summary>
/// Calls a tool of an MCP server that <c>find_tools</c> returned, without the tool having been listed in the request.
/// </summary>
/// <remarks>
/// The harness never runs this tool. It resolves the call to the tool it names and checks permissions and approvals for that tool, as for any other call.
/// </remarks>
internal sealed class UseToolTool : IForwardingTool
{
    public const string ToolName = "use_tool";

    private readonly McpManager _manager;
    private readonly IReadOnlyList<string> _servers;

    public UseToolTool(McpManager manager, IReadOnlyList<string> servers)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(servers);
        _manager = manager;
        _servers = servers;
    }

    public string Name => ToolName;

    public string Description => "Call a tool that find_tools returned. Pass the tool's exact name and an object with its arguments, as its input schema describes.";

    public string InputSchema => """
        {
          "type": "object",
          "properties": {
            "name": { "type": "string", "description": "The exact tool name from find_tools." },
            "arguments": { "type": "object", "description": "The arguments for the tool." }
          },
          "required": ["name"]
        }
        """;

    public bool IsReadOnly => false;

    public TimeSpan Timeout { get; } = TimeSpan.FromMinutes(2);

    public string Summarize(JsonElement arguments) => ToolSupport.Text(arguments, "name");

    public async Task<ForwardedCall> ResolveAsync(ToolArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var name = arguments.RequiredString("name").Trim();
        var json = ArgumentsOf(arguments.OptionalElement("arguments"));
        var lookup = await _manager.FindToolAsync(name, _servers, cancellationToken);
        return new ForwardedCall(lookup.Tool, json, lookup.Tool?.Name ?? name, lookup.Problem);
    }

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(ToolResult.Error("This call was not resolved to the tool it names, so nothing ran."));
    }

    /// <summary>
    /// Normalize forwarded arguments to a JSON object string.
    /// </summary>
    /// <remarks>Some models send the arguments as JSON text instead of an object.</remarks>
    /// <param name="element">The forwarded arguments, or null for an empty object.</param>
    private static string ArgumentsOf(JsonElement? element)
    {
        switch (element)
        {
            case null:
                return "{}";
            case { ValueKind: JsonValueKind.Object } value:
                return value.GetRawText();
            case { ValueKind: JsonValueKind.String } value:
                try
                {
                    using var parsed = JsonDocument.Parse(value.GetString() ?? "{}");
                    if (parsed.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        return parsed.RootElement.GetRawText();
                    }
                }
                catch (JsonException)
                {
                    // Falls through to the error below, which names the problem for the model.
                }

                break;
        }

        throw new ToolArgumentException("The 'arguments' argument must be an object.");
    }
}
