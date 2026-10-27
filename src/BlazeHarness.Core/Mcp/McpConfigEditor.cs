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
    /// Add the servers of a pasted snippet to the text of the file.
    /// </summary>
    /// <remarks>
    /// The snippet may be a whole <c>mcp.json</c> (with <c>mcpServers</c> or <c>servers</c>), or just a map of server names to their settings,
    /// with or without the outer braces. A server with a name the file already uses replaces that server.
    /// </remarks>
    /// <param name="json">The file's text, which may be empty.</param>
    /// <param name="snippet">The pasted text.</param>
    /// <returns>The new text and the names that were added and replaced, or an error to show the user.</returns>
    public static McpMergeResult Merge(string json, string snippet)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(snippet);

        var pasted = ParseObject(snippet) ?? ParseObject("{" + snippet.Trim().TrimEnd(',') + "}");
        if (pasted is null)
        {
            return McpMergeResult.Failed("The pasted text is not valid JSON.");
        }

        var incoming = (pasted["mcpServers"] ?? pasted["servers"]) as JsonObject ?? pasted;
        if (incoming.Count == 0 || incoming.Any(pair => pair.Value is not JsonObject))
        {
            return McpMergeResult.Failed("The pasted text has no servers. Each server needs a name and an object with its settings.");
        }

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
            return McpMergeResult.Failed("The file is not valid JSON, so the servers can't be added to it. Fix the file first.");
        }

        if ((top["mcpServers"] ?? top["servers"]) is not JsonObject target)
        {
            target = [];
            top["mcpServers"] = target;
        }

        List<string> added = [];
        List<string> replaced = [];
        foreach (var (name, server) in incoming.ToList())
        {
            (target.ContainsKey(name) ? replaced : added).Add(name);
            target[name] = server!.DeepClone();
        }

        return new McpMergeResult(top.ToJsonString(Indented), added, replaced, null);
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
