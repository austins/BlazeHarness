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
}
