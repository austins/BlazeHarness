using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// Creates a file or replaces its whole content.
/// </summary>
public sealed class WriteFileTool : ITool
{
    private const int SampleBytes = 8192;

    /// <inheritdoc />
    public string Name => "write_file";

    /// <inheritdoc />
    public string Description =>
        "Create a file inside the working folder, or replace an existing file completely. Missing folders are created. " +
        "To change part of an existing file, use edit_file instead. " +
        "A file that uses CRLF line endings keeps them when the new content uses plain line feeds.";

    /// <inheritdoc />
    public string InputSchema => """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "The file path, relative to the working folder." },
            "content": { "type": "string", "description": "The complete new content of the file." }
          },
          "required": ["path", "content"]
        }
        """;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public IReadOnlyList<string> PathArguments { get; } = ["path"];

    /// <inheritdoc />
    public TimeSpan Timeout { get; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public string Summarize(JsonElement arguments) => ToolSupport.Text(arguments, "path");

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(context);

        var content = arguments.RequiredString("content");
        if (!context.TryResolve(arguments.RequiredString("path"), out var full, out var error))
        {
            return Task.FromResult(ToolResult.Error(error));
        }

        if (ToolSupport.FolderInsteadOfFile(context, full) is { } folder)
        {
            return Task.FromResult(ToolResult.Error(folder));
        }

        var existed = File.Exists(full);
        var keepBom = existed && StartsWithUtf8Bom(full);
        var text = existed && !content.Contains('\r', StringComparison.Ordinal) && UsesCrlf(full)
            ? content.Replace("\n", "\r\n", StringComparison.Ordinal)
            : content;
        var bytes = new UTF8Encoding(keepBom).GetBytes(text);
        if (keepBom)
        {
            bytes = [.. Encoding.UTF8.GetPreamble(), .. bytes];
        }

        AtomicFile.Write(full, bytes);
        var lines = content.Length == 0 ? 0 : content.Count(c => c == '\n') + (content.EndsWith('\n') ? 0 : 1);
        return Task.FromResult(ToolResult.Ok(string.Create(
            CultureInfo.InvariantCulture,
            $"{(existed ? "Replaced" : "Created")} {context.Relative(full)} ({lines} lines, {bytes.Length} bytes).")));
    }

    private static bool UsesCrlf(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[SampleBytes];
        var read = stream.Read(buffer);
        var first = buffer.AsSpan(0, read).IndexOf((byte)'\n');
        return first > 0 && buffer[first - 1] == '\r';
    }

    private static bool StartsWithUtf8Bom(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> head = stackalloc byte[3];
        return stream.Read(head) == 3 && head.SequenceEqual(Encoding.UTF8.GetPreamble());
    }
}
