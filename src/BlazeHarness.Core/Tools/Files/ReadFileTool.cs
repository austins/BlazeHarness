using BlazeHarness.Core.Attachments;
using Microsoft.Extensions.AI;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BlazeHarness.Core.Tools.Files;

/// <summary>
/// Reads a text file, a range of lines at a time, or hands an image file to the model as a picture.
/// </summary>
public sealed class ReadFileTool : ITool
{
    /// <summary>
    /// The name of the tool.
    /// </summary>
    public const string ToolName = "read_file";

    /// <summary>
    /// The argument that names the file.
    /// </summary>
    public const string PathArgument = "path";

    /// <summary>
    /// The text that starts the note after the lines when a call shows only part of the file.
    /// </summary>
    public const string RangeNoteStart = "\n[Showing lines ";

    /// <summary>
    /// The number of lines a call reads when it gives no limit.
    /// </summary>
    internal const int DefaultLimit = 2000;

    /// <summary>
    /// The most lines one call may ask for.
    /// </summary>
    internal const int MaxLimit = 10_000;

    private const int MaxLineCharacters = 2000;
    private const int CharacterBudget = ToolSet.MaxOutputCharacters - 500;

    /// <inheritdoc />
    public string Name => ToolName;

    /// <inheritdoc />
    public string Description =>
        $"Read a text file inside the working folder. Returns up to {DefaultLimit} lines at a time; for a longer file, " +
        "pass offset (the first line to read, starting at 1) and limit to read it in pieces. A PNG, JPEG, GIF or WebP image " +
        $"up to {ImageFiles.MaxViewableBytes / (1024 * 1024)} MB comes back as a picture you can look at; other binary files can't be read.";

    /// <inheritdoc />
    public string InputSchema => $$"""
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "The file path, relative to the working folder." },
            "offset": { "type": "integer", "minimum": 1, "description": "The first line to read, starting at 1." },
            "limit": { "type": "integer", "minimum": 1, "maximum": {{MaxLimit}}, "description": "The most lines to read." }
          },
          "required": ["path"]
        }
        """;

    /// <inheritdoc />
    public bool IsReadOnly => true;

    /// <inheritdoc />
    public IReadOnlyList<string> PathArguments { get; } = [PathArgument];

    /// <inheritdoc />
    public TimeSpan Timeout { get; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public string Summarize(JsonElement arguments) => ToolSupport.Text(arguments, PathArgument);

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(context);

        var offset = arguments.OptionalInt("offset", minimum: 1) ?? 1;
        var limit = arguments.OptionalInt("limit", minimum: 1, maximum: MaxLimit) ?? DefaultLimit;
        if (!context.TryResolve(arguments.RequiredString(PathArgument), out var full, out var error))
        {
            return Task.FromResult(ToolResult.Error(error));
        }

        if (ToolSupport.FolderInsteadOfFile(context, full) is { } folder)
        {
            return Task.FromResult(ToolResult.Error(folder));
        }

        if (!File.Exists(full))
        {
            return Task.FromResult(ToolResult.Error($"The file '{context.Relative(full)}' does not exist."));
        }

        if (ToolSupport.IsEmpty(full))
        {
            return Task.FromResult(ToolResult.Ok($"[{context.Relative(full)} is empty.]"));
        }

        if (ReadImage(context.Relative(full), full) is { } image)
        {
            return Task.FromResult(image);
        }

        return Task.FromResult(ToolSupport.LooksBinary(full)
            ? ToolResult.Error($"'{context.Relative(full)}' is a binary file ({new FileInfo(full).Length} bytes), which can't be read as text.")
            : ReadLines(context.Relative(full), full, offset, limit, cancellationToken));
    }

    internal static ToolResult ReadLines(string display, string full, int offset, int limit, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(full, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return ReadLines(display, reader, offset, limit, cancellationToken);
    }

    internal static ToolResult ReadLines(string display, TextReader reader, int offset, int limit, CancellationToken cancellationToken)
    {
        var lines = new CappedLineReader(reader, MaxLineCharacters);
        var shown = new StringBuilder();
        var total = 0;
        var firstShown = 0;
        var lastShown = 0;
        var count = 0;
        var hasMore = false;
        while (lines.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total++;
            if (total < offset)
            {
                continue;
            }

            var text = lines.DroppedCharacters > 0 ? line + " [line cut]" : line;
            if (count >= limit || (count > 0 && shown.Length + text.Length + 1 > CharacterBudget))
            {
                hasMore = true;
                break;
            }

            if (count > 0)
            {
                shown.Append('\n');
            }
            else
            {
                firstShown = total;
            }

            shown.Append(text);
            lastShown = total;
            count++;
        }

        if (total == 0)
        {
            return ToolResult.Ok($"[{display} is empty.]");
        }

        if (firstShown == 0)
        {
            return ToolResult.Error(string.Create(CultureInfo.InvariantCulture, $"The offset {offset} is past the end of '{display}', which has {total} lines."));
        }

        if (firstShown > 1 || hasMore)
        {
            shown.Append(RangeNoteStart).Append(CultureInfo.InvariantCulture, $"{firstShown}-{lastShown}");
            if (!hasMore)
            {
                shown.Append(string.Create(CultureInfo.InvariantCulture, $" of {total}"));
            }

            shown.Append('.');
            shown.Append(hasMore ? string.Create(CultureInfo.InvariantCulture, $" Call again with offset={lastShown + 1} to continue.]") : "]");
        }

        return ToolResult.Ok(shown.ToString());
    }

    private static ToolResult? ReadImage(string display, string full)
    {
        Span<byte> head = stackalloc byte[ImageFiles.SniffBytes];
        int read;
        using (var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        }

        if (ImageFiles.SniffRaster(head[..read]) is not { } image)
        {
            return null;
        }

        var size = new FileInfo(full).Length;
        if (size > ImageFiles.MaxViewableBytes)
        {
            return ToolResult.Error(string.Create(
                CultureInfo.InvariantCulture,
                $"'{display}' is an image of {FileSize.Format(size)}, larger than the {ImageFiles.MaxViewableBytes / (1024 * 1024)} MB a picture can be."));
        }

        return ToolResult.WithImage(
            string.Create(CultureInfo.InvariantCulture, $"[{display} is an image ({image.MediaType}, {FileSize.Format(size)}); the picture follows.]"),
            new DataContent(File.ReadAllBytes(full), image.MediaType));
    }
}
