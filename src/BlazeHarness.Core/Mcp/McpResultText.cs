using BlazeHarness.Core.Tools;
using ModelContextProtocol.Protocol;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BlazeHarness.Core.Mcp;

/// <summary>
/// Turns a server's tool result into the plain text the model reads.
/// </summary>
internal static class McpResultText
{
    public static ToolResult Convert(CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var text = new StringBuilder();
        foreach (var block in result.Content)
        {
            if (text.Length > 0)
            {
                text.Append('\n');
            }

            text.Append(Describe(block));
        }

        if (text.Length == 0 && result.StructuredContent is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } structured)
        {
            text.Append(structured.GetRawText());
        }

        if (text.Length == 0)
        {
            text.Append(result.IsError == true ? "The tool failed without saying why." : "The tool returned no output.");
        }

        return result.IsError == true ? ToolResult.Error(text.ToString()) : ToolResult.Ok(text.ToString());
    }

    private static string Describe(ContentBlock block)
    {
        return block switch
        {
            TextContentBlock text => text.Text,
            ImageContentBlock image => string.Create(CultureInfo.InvariantCulture, $"[An image ({image.MimeType}) was returned; images from MCP tools are not shown to the model.]"),
            AudioContentBlock audio => string.Create(CultureInfo.InvariantCulture, $"[Audio ({audio.MimeType}) was returned; it is not shown to the model.]"),
            ResourceLinkBlock link => string.Create(CultureInfo.InvariantCulture, $"[Resource link: {link.Uri}{(string.IsNullOrEmpty(link.Name) ? string.Empty : " (" + link.Name + ")")}]"),
            EmbeddedResourceBlock { Resource: TextResourceContents resource } => resource.Text,
            EmbeddedResourceBlock { Resource: { } resource } => string.Create(CultureInfo.InvariantCulture, $"[A binary resource was returned: {resource.Uri}]"),
            _ => string.Create(CultureInfo.InvariantCulture, $"[A '{block.Type}' block was returned, which is not shown to the model.]"),
        };
    }
}
