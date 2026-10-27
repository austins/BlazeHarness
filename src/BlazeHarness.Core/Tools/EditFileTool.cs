using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// Replaces an exact piece of text in a file.
/// </summary>
public sealed class EditFileTool : ITool
{
    private const long MaxFileBytes = 10 * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <inheritdoc />
    public string Name => "edit_file";

    /// <inheritdoc />
    public string Description =>
        "Replace an exact piece of text in an existing file inside the working folder. old_string must match the file exactly, " +
        "including whitespace, and must occur once unless replace_all is true; include enough surrounding lines to make it unique. " +
        "Line endings are matched loosely, so a CRLF file can be edited with LF text and keeps its CRLF endings.";

    /// <inheritdoc />
    public string InputSchema => """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "The file path, relative to the working folder." },
            "old_string": { "type": "string", "description": "The exact text to replace." },
            "new_string": { "type": "string", "description": "The text to put in its place." },
            "replace_all": { "type": "boolean", "description": "Replace every occurrence instead of requiring exactly one." }
          },
          "required": ["path", "old_string", "new_string"]
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

        var oldText = arguments.RequiredString("old_string");
        var newText = arguments.RequiredString("new_string");
        var replaceAll = arguments.OptionalBool("replace_all");
        if (!context.TryResolve(arguments.RequiredString("path"), out var full, out var error))
        {
            return Task.FromResult(ToolResult.Error(error));
        }

        return Task.FromResult(Edit(context, full, oldText, newText, replaceAll));
    }

    private static ToolResult Edit(ToolContext context, string full, string oldText, string newText, bool replaceAll)
    {
        var display = context.Relative(full);
        if (ToolSupport.FolderInsteadOfFile(context, full) is { } folder)
        {
            return ToolResult.Error(folder);
        }

        if (!File.Exists(full))
        {
            return ToolResult.Error($"The file '{display}' does not exist. Use write_file to create it.");
        }

        if (oldText.Length == 0)
        {
            return ToolResult.Error("old_string is empty. Use write_file to replace a whole file.");
        }

        var from = oldText.Replace("\r\n", "\n", StringComparison.Ordinal);
        var to = newText.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (from == to)
        {
            return ToolResult.Error("old_string and new_string are the same, so nothing would change.");
        }

        if (ToolSupport.IsEmpty(full))
        {
            return ToolResult.Error($"'{display}' is empty, so old_string can't be in it. Use write_file to put text in it.");
        }

        if (new FileInfo(full).Length > MaxFileBytes)
        {
            return ToolResult.Error($"'{display}' is larger than {MaxFileBytes / (1024 * 1024)} MiB, which is too big to edit with this tool.");
        }

        var bytes = File.ReadAllBytes(full);
        if (ToolSupport.LooksBinary(bytes) || HasUtf16Or32Bom(bytes))
        {
            return ToolResult.Error($"'{display}' is not a UTF-8 text file, so it can't be edited with this tool.");
        }

        var hasBom = bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble());
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
        }
        catch (DecoderFallbackException)
        {
            return ToolResult.Error($"'{display}' is not valid UTF-8, so it can't be edited with this tool.");
        }

        // Only a file that uses CRLF throughout is converted back; a mixed file is edited as it is, so its other lines keep their endings.
        var crlf = text.Contains("\r\n", StringComparison.Ordinal) && !HasBareLineFeed(text);
        var normalized = crlf ? text.Replace("\r\n", "\n", StringComparison.Ordinal) : text;

        var count = CountOccurrences(normalized, from);
        if (count == 0)
        {
            return ToolResult.Error($"old_string was not found in '{display}'. It must match the file exactly, including whitespace; read the file again to check.");
        }

        if (count > 1 && !replaceAll)
        {
            return ToolResult.Error(string.Create(
                CultureInfo.InvariantCulture,
                $"old_string occurs {count} times in '{display}'. Add surrounding lines to make it unique, or set replace_all to true."));
        }

        var edited = normalized.Replace(from, to, StringComparison.Ordinal);
        if (crlf)
        {
            edited = edited.Replace("\n", "\r\n", StringComparison.Ordinal);
        }

        var output = new UTF8Encoding(false).GetBytes(edited);
        AtomicFile.Write(full, hasBom ? [.. Encoding.UTF8.GetPreamble(), .. output] : output);
        return ToolResult.Ok(string.Create(CultureInfo.InvariantCulture, $"Edited {display}: replaced {count} {(count == 1 ? "occurrence" : "occurrences")}."));
    }

    private static bool HasBareLineFeed(string text)
    {
        for (var index = text.IndexOf('\n', StringComparison.Ordinal); index >= 0; index = text.IndexOf('\n', index + 1))
        {
            if (index == 0 || text[index - 1] != '\r')
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasUtf16Or32Bom(byte[] bytes) => ToolSupport.HasUnicodeBom(bytes) && !bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble());

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
