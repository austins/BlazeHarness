using System.Text.Json;
using System.Text.Json.Nodes;

namespace BlazeHarness.Core.Mcp;

/// <summary>
/// Changes one thing in an <c>mcp.json</c> text and leaves the rest as the user, or another tool, wrote it.
/// </summary>
/// <remarks>
/// The file is read as JSON and written back, so unknown fields and their order survive, but spacing is normalized.
/// </remarks>
public static class McpConfigEditor
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// Turn a server on or off for every chat by setting its <c>disabled</c> field.
    /// </summary>
    /// <param name="json">The file's text.</param>
    /// <param name="server">The name of the server.</param>
    /// <param name="disabled">True to turn it off. Turning it on removes the field, as other tools do.</param>
    /// <returns>The new text, or null when the text is not valid or has no such server.</returns>
    public static string? SetDisabled(string json, string server, bool disabled)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(server);

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return null;
        }

        if (root is not JsonObject top || ((top["mcpServers"] ?? top["servers"]) as JsonObject)?[server] is not JsonObject entry)
        {
            return null;
        }

        if (disabled)
        {
            entry["disabled"] = true;
        }
        else
        {
            entry.Remove("disabled");
        }

        return root.ToJsonString(Indented);
    }

    /// <summary>
    /// Add a server to the text of the file, or change the one with the same name.
    /// </summary>
    /// <remarks>
    /// A server that is already in the file keeps the fields this editor doesn't know, such as <c>disabled</c>. Fields that don't apply to
    /// the server's transport are removed.
    /// </remarks>
    /// <param name="json">The file's text, which may be empty.</param>
    /// <param name="server">The server's settings, with values as they are to be written.</param>
    /// <returns>The new text and whether the server was added or replaced, or an error to show the user.</returns>
    public static McpMergeResult Upsert(string json, McpServerConfig server)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(server);

        JsonObject top;
        if (string.IsNullOrWhiteSpace(json))
        {
            top = [];
        }
        else if (ParseObject(json) is { } existing)
        {
            top = existing;
        }
        else
        {
            return McpMergeResult.Failed("The file is not valid JSON, so the server can't be saved to it. Fix the file first.");
        }

        if ((top["mcpServers"] ?? top["servers"]) is not JsonObject target)
        {
            target = [];
            top["mcpServers"] = target;
        }

        var found = target[server.Name] is JsonObject;
        var entry = target[server.Name] as JsonObject ?? [];
        if (server.Transport == McpTransport.Stdio)
        {
            entry.Remove("type");
            entry.Remove("url");
            entry.Remove("headers");
            entry["command"] = server.Command;
            SetList(entry, "args", server.Args);
            SetMap(entry, "env", server.Env);
            SetText(entry, "cwd", server.Cwd);
        }
        else
        {
            entry.Remove("command");
            entry.Remove("args");
            entry.Remove("env");
            entry.Remove("cwd");
            entry["type"] = server.Transport == McpTransport.Sse ? "sse" : "http";
            entry["url"] = server.Url;
            SetMap(entry, "headers", server.Headers);
        }

        target[server.Name] = entry;
        return new McpMergeResult(top.ToJsonString(Indented), found ? [] : [server.Name], found ? [server.Name] : [], null);
    }

    /// <summary>
    /// Take a server out of the text of the file.
    /// </summary>
    /// <param name="json">The file's text.</param>
    /// <param name="server">The name of the server.</param>
    /// <returns>The new text, or null when the text is not valid or has no such server.</returns>
    public static string? Remove(string json, string server)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(server);

        if (ParseObject(json) is not { } top || ((top["mcpServers"] ?? top["servers"]) as JsonObject) is not { } servers || !servers.Remove(server))
        {
            return null;
        }

        return top.ToJsonString(Indented);
    }

    private static void SetText(JsonObject entry, string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            entry.Remove(key);
        }
        else
        {
            entry[key] = value;
        }
    }

    private static void SetList(JsonObject entry, string key, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            entry.Remove(key);
        }
        else
        {
            entry[key] = new JsonArray([.. values.Select(v => (JsonNode?)JsonValue.Create(v))]);
        }
    }

    private static void SetMap(JsonObject entry, string key, IReadOnlyDictionary<string, string> values)
    {
        if (values.Count == 0)
        {
            entry.Remove(key);
        }
        else
        {
            var map = new JsonObject();
            foreach (var (name, value) in values)
            {
                map[name] = value;
            }

            entry[key] = map;
        }
    }

    private static JsonObject? ParseObject(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return null;
        }
    }
}
