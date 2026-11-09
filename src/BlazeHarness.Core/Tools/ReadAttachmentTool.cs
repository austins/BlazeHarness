using BlazeHarness.Core.Attachments;
using BlazeHarness.Core.Tools.Files;
using Microsoft.Extensions.AI;
using System.Globalization;
using System.Text.Json;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// Reads a file the user attached to the chat.
/// </summary>
internal sealed class ReadAttachmentTool : ITool
{
    private readonly ChatAttachments _attachments;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReadAttachmentTool"/> class.
    /// </summary>
    /// <param name="attachments">The chat's attachments, which are the only files it can read.</param>
    public ReadAttachmentTool(ChatAttachments attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        _attachments = attachments;
    }

    /// <inheritdoc />
    public string Name => "read_attachment";

    /// <inheritdoc />
    public string Description =>
        $"Read a file attached to this chat, by its id: a file the user attached, or a picture a tool produced. Returns up to {ReadFileTool.DefaultLimit} lines " +
        "of a text file at a time; for a longer file, pass offset (the first line to read, starting at 1) and limit to read it in pieces. " +
        $"A picture up to {ImageFiles.MaxViewableBytes / (1024 * 1024)} MB comes back for you to look at.";

    /// <inheritdoc />
    public string InputSchema => $$"""
        {
          "type": "object",
          "properties": {
            "id": { "type": "string", "description": "The attachment id given in the message." },
            "offset": { "type": "integer", "minimum": 1, "description": "The first line to read, starting at 1." },
            "limit": { "type": "integer", "minimum": 1, "maximum": {{ReadFileTool.MaxLimit}}, "description": "The most lines to read." }
          },
          "required": ["id"]
        }
        """;

    /// <inheritdoc />
    public bool IsReadOnly => true;

    /// <inheritdoc />
    public TimeSpan Timeout { get; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public string Summarize(JsonElement arguments)
    {
        var id = ToolSupport.Text(arguments, "id");
        return _attachments.Find(id)?.Name ?? id;
    }

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var offset = arguments.OptionalInt("offset", minimum: 1) ?? 1;
        var limit = arguments.OptionalInt("limit", minimum: 1, maximum: ReadFileTool.MaxLimit) ?? ReadFileTool.DefaultLimit;
        var id = arguments.RequiredString("id");
        if (_attachments.Find(id) is not { } file)
        {
            return Task.FromResult(ToolResult.Error($"There is no attachment with the id '{id}'."));
        }

        return Task.FromResult(file.MediaType.StartsWith("image/", StringComparison.Ordinal)
            ? ReadImage(id, file)
            : ReadFileTool.ReadLines(file.Name, file.Path, offset, limit, cancellationToken));
    }

    /// <summary>
    /// Return an attached picture for the model to look at.
    /// </summary>
    /// <remarks>The result names the attachment it came from, so the chat shows the picture without saving another copy.</remarks>
    private static ToolResult ReadImage(string id, AttachmentFile file)
    {
        var size = new FileInfo(file.Path).Length;
        if (size > ImageFiles.MaxViewableBytes)
        {
            return ToolResult.Error(string.Create(
                CultureInfo.InvariantCulture,
                $"'{file.Name}' is a picture of {FileSize.Format(size)}, larger than the {ImageFiles.MaxViewableBytes / (1024 * 1024)} MB a picture can be."));
        }

        var attachment = new AttachmentInfo(id, file.Name, file.MediaType, size, 0);
        var output = string.Create(CultureInfo.InvariantCulture, $"[{file.Name} is a picture ({file.MediaType}, {FileSize.Format(size)}); it follows the tool results.]");
        return ToolResult.Ok(output) with { Images = [new ToolImage(new DataContent(File.ReadAllBytes(file.Path), file.MediaType), attachment)] };
    }
}
