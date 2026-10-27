using BlazeHarness.Core.Tools;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BlazeHarness.Core.Mcp;

/// <summary>
/// Offers one tool of an MCP server through the same abstraction as the built-in tools.
/// </summary>
internal sealed class McpTool : IMcpTool
{
    internal const int MaxDescriptionCharacters = 400;

    private const int MaxSummaryCharacters = 120;
    private const int MaxSummaryValueCharacters = 40;

    // Long enough for a slow server call, short enough that a hung one does not stall the run.
    private static readonly TimeSpan CallTimeout = TimeSpan.FromMinutes(2);

    private readonly IMcpConnection _connection;

    public McpTool(string name, string serverName, McpToolInfo info, IMcpConnection connection)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(serverName);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(connection);
        Name = name;
        ServerName = serverName;
        RemoteName = info.Name;
        ReadOnlyHint = info.ReadOnlyHint;
        DestructiveHint = info.DestructiveHint;
        Description = TrimDescription(info.Description);
        InputSchema = NormalizeSchema(info.InputSchema);
        _connection = connection;
    }

    public string Name { get; }

    public string ServerName { get; }

    public string RemoteName { get; }

    public bool? ReadOnlyHint { get; }

    public bool? DestructiveHint { get; }

    public string Description { get; }

    public string InputSchema { get; }

    // The harness cannot see what a server does, so none of its tools may run alongside others on a hint.
    public bool IsReadOnly => false;

    public TimeSpan Timeout { get; } = CallTimeout;

    public static string TrimDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return string.Empty;
        }

        var text = string.Join(' ', description.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= MaxDescriptionCharacters ? text : text[..(MaxDescriptionCharacters - 1)].TrimEnd() + "…";
    }

    public static string NormalizeSchema(string? schema)
    {
        const string Empty = """{"type":"object","properties":{}}""";
        if (string.IsNullOrWhiteSpace(schema))
        {
            return Empty;
        }

        try
        {
            if (JsonNode.Parse(schema) is not JsonObject root)
            {
                return Empty;
            }

            if (root["type"]?.GetValue<string>() != "object")
            {
                root["type"] = "object";
            }

            root["properties"] ??= new JsonObject();
            return root.ToJsonString();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return Empty;
        }
    }

    public string Summarize(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
#pragma warning disable IDISP004 // The enumerator of a JSON object holds nothing that needs disposing.
        foreach (var property in arguments.EnumerateObject())
#pragma warning restore IDISP004
        {
            if (text.Length > 0)
            {
                text.Append(", ");
            }

            var value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : property.Value.GetRawText();
            value = value.ReplaceLineEndings(" ");
            text.Append(property.Name).Append('=').Append(value.Length <= MaxSummaryValueCharacters ? value : value[..MaxSummaryValueCharacters] + "…");
            if (text.Length > MaxSummaryCharacters)
            {
                break;
            }
        }

        return text.Length <= MaxSummaryCharacters ? text.ToString() : text.ToString(0, MaxSummaryCharacters) + "…";
    }

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return _connection.CallToolAsync(RemoteName, arguments.ToJson(), cancellationToken);
    }
}
