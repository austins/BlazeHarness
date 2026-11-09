using BlazeHarness.Core.Attachments;
using BlazeHarness.Core.Tools;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BlazeHarness.Core.Mcp.Tools;

/// <summary>
/// Turns a server's tool result into the text and pictures the model reads.
/// </summary>
/// <remarks>
/// PNG, JPEG, GIF and WebP pictures are passed on; the chat keeps a copy of each and decides which are shown. Other content that is not text,
/// such as audio, is named instead. Structured content is added when no text block already holds the same JSON.
/// </remarks>
internal static class McpResultText
{
    public static ToolResult Convert(CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var text = new StringBuilder();
        List<ToolImage> images = [];
        var pictures = result.Content.Count(b => b is ImageContentBlock);
        foreach (var block in result.Content)
        {
            if (text.Length > 0)
            {
                text.Append('\n');
            }

            text.Append(block is ImageContentBlock image ? DescribeImage(image, images, pictures) : Describe(block));
        }

        if (result.StructuredContent is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } structured
            && !result.Content.Any(b => b is TextContentBlock block && SameJson(block.Text, structured)))
        {
            text.Append(text.Length > 0 ? "\nStructured result:\n" : string.Empty).Append(structured.GetRawText());
        }

        if (text.Length == 0)
        {
            text.Append(result.IsError == true ? "The tool failed without saying why." : "The tool returned no output.");
        }

        var converted = result.IsError == true ? ToolResult.Error(text.ToString()) : ToolResult.Ok(text.ToString());
        return converted with { Images = images };
    }

    private static string DescribeImage(ImageContentBlock image, List<ToolImage> images, int count)
    {
        ReadOnlyMemory<byte> data;
        try
        {
            data = image.DecodedData;
        }
        catch (FormatException)
        {
            return "[A picture was returned that could not be decoded.]";
        }

        if (data.Length > ChatAttachments.MaxFileBytes || ImageFiles.SniffRaster(data.Span) is not { } sniffed)
        {
            return string.Create(CultureInfo.InvariantCulture, $"[A picture ({image.MimeType}, {FileSize.Format(data.Length)}) was returned that can't be shown; only PNG, JPEG, GIF and WebP pictures up to {ChatAttachments.MaxFileBytes / (1024 * 1024)} MB can.]");
        }

        images.Add(new ToolImage(new DataContent(data, sniffed.MediaType)));
        var name = count > 1 ? string.Create(CultureInfo.InvariantCulture, $"Picture {images.Count} of {count}") : "A picture";
        return string.Create(CultureInfo.InvariantCulture, $"[{name} ({sniffed.MediaType}, {FileSize.Format(data.Length)}) follows the tool results.]");
    }

    private static bool SameJson(string text, JsonElement structured)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return JsonElement.DeepEquals(document.RootElement, structured);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Describe(ContentBlock block)
    {
        return block switch
        {
            TextContentBlock text => text.Text,
            AudioContentBlock audio => string.Create(CultureInfo.InvariantCulture, $"[Audio ({audio.MimeType}) was returned; it is not shown to the model.]"),
            ResourceLinkBlock link => string.Create(CultureInfo.InvariantCulture, $"[Resource link: {link.Uri}{(string.IsNullOrEmpty(link.Name) ? string.Empty : " (" + link.Name + ")")}]"),
            EmbeddedResourceBlock { Resource: TextResourceContents resource } => resource.Text,
            EmbeddedResourceBlock { Resource: { } resource } => string.Create(CultureInfo.InvariantCulture, $"[A binary resource was returned: {resource.Uri}]"),
            _ => string.Create(CultureInfo.InvariantCulture, $"[A '{block.Type}' block was returned, which is not shown to the model.]"),
        };
    }
}
