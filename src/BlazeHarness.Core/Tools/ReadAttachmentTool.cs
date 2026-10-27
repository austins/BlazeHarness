using BlazeHarness.Core.Attachments;
using System.Text.Json;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// Reads a file the user attached to the session.
/// </summary>
internal sealed class ReadAttachmentTool : ITool
{
    private readonly SessionAttachments _attachments;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReadAttachmentTool"/> class.
    /// </summary>
    /// <param name="attachments">The session's attachments, which are the only files it can read.</param>
    public ReadAttachmentTool(SessionAttachments attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        _attachments = attachments;
    }

    /// <inheritdoc />
    public string Name => "read_attachment";

    /// <inheritdoc />
    public string Description =>
        $"Read a text file the user attached to this chat, by its id. Returns up to {ReadFileTool.DefaultLimit} lines at a time; for a longer file, " +
        "pass offset (the first line to read, starting at 1) and limit to read it in pieces.";

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
            ? ToolResult.Error($"'{file.Name}' is an image, which can't be read as text.")
            : ReadFileTool.ReadLines(file.Name, file.Path, offset, limit, cancellationToken));
    }
}
