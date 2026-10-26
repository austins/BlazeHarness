using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// Reads a text file, a range of lines at a time.
/// </summary>
public sealed class ReadFileTool : ITool
{
    private const int DefaultLimit = 2000;
    private const int MaxLineCharacters = 2000;
    private const int CharacterBudget = ToolSet.MaxOutputCharacters - 500;

    /// <inheritdoc />
    public string Name => "read_file";

    /// <inheritdoc />
    public string Description =>
        "Read a text file inside the working folder. Returns up to 2000 lines at a time; for a longer file, " +
        "pass offset (the first line to read, starting at 1) and limit to read it in pieces. Binary files can't be read.";

    /// <inheritdoc />
    public string InputSchema => """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "The file path, relative to the working folder." },
            "offset": { "type": "integer", "minimum": 1, "description": "The first line to read, starting at 1." },
            "limit": { "type": "integer", "minimum": 1, "maximum": 10000, "description": "The most lines to read." }
          },
          "required": ["path"]
        }
        """;

    /// <inheritdoc />
    public bool IsReadOnly => true;

    /// <inheritdoc />
    public TimeSpan Timeout { get; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public string Summarize(JsonElement arguments) => ToolSupport.Text(arguments, "path");

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(context);

        var offset = arguments.OptionalInt("offset", minimum: 1) ?? 1;
        var limit = arguments.OptionalInt("limit", minimum: 1, maximum: 10_000) ?? DefaultLimit;
        if (!context.TryResolve(arguments.RequiredString("path"), out var full, out var error))
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

        return Task.FromResult(ToolSupport.LooksBinary(full)
            ? ToolResult.Error($"'{context.Relative(full)}' is a binary file ({new FileInfo(full).Length} bytes), which can't be read as text.")
            : ReadLines(context.Relative(full), full, offset, limit, cancellationToken));
    }

    private static ToolResult ReadLines(string display, string full, int offset, int limit, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(full, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var shown = new StringBuilder();
        var total = 0;
        var firstShown = 0;
        var lastShown = 0;
        var count = 0;
        var budgetReached = false;
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total++;
            if (total < offset || budgetReached)
            {
                continue;
            }

            var text = line.Length > MaxLineCharacters ? line[..MaxLineCharacters] + " [line cut]" : line;
            if (count >= limit || (count > 0 && shown.Length + text.Length + 1 > CharacterBudget))
            {
                budgetReached = true;
                continue;
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

        if (firstShown > 1 || lastShown < total)
        {
            shown.Append(string.Create(CultureInfo.InvariantCulture, $"\n[Showing lines {firstShown}-{lastShown} of {total}."));
            shown.Append(lastShown < total ? string.Create(CultureInfo.InvariantCulture, $" Call again with offset={lastShown + 1} to continue.]") : "]");
        }

        return ToolResult.Ok(shown.ToString());
    }
}
