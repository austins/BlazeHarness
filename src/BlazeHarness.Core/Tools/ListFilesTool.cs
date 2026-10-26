using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// Lists the files and folders inside the working folder.
/// </summary>
public sealed class ListFilesTool : ITool
{
    private const int DefaultLimit = 200;

    /// <inheritdoc />
    public string Name => "list_files";

    /// <inheritdoc />
    public string Description =>
        "List files and folders inside the working folder, one per line; folders end with a slash. " +
        "Files that .gitignore excludes are left out. With a pattern such as '**/*.cs' or 'src/*.ts' it searches subfolders too. " +
        "A pattern without a slash matches names at any depth.";

    /// <inheritdoc />
    public string InputSchema => """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "The folder to list, relative to the working folder. Defaults to the working folder." },
            "pattern": { "type": "string", "description": "A glob pattern to match, relative to the folder." },
            "recursive": { "type": "boolean", "description": "Include subfolders. Defaults to true when a pattern is given, otherwise false." },
            "include_ignored": { "type": "boolean", "description": "Also list files that .gitignore excludes." },
            "max_results": { "type": "integer", "minimum": 1, "maximum": 1000, "description": "The most entries to return. Defaults to 200." }
          }
        }
        """;

    /// <inheritdoc />
    public bool IsReadOnly => true;

    /// <inheritdoc />
    public TimeSpan Timeout { get; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public string Summarize(JsonElement arguments)
    {
        var pattern = ToolSupport.Text(arguments, "pattern");
        var path = ToolSupport.Text(arguments, "path");
        return pattern.Length > 0 ? (path.Length > 0 ? $"{pattern} in {path}" : pattern) : path.Length > 0 ? path : ".";
    }

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(context);

        var pattern = arguments.OptionalString("pattern");
        var recursive = arguments.OptionalBool("recursive", defaultValue: !string.IsNullOrEmpty(pattern));
        var includeIgnored = arguments.OptionalBool("include_ignored");
        var limit = arguments.OptionalInt("max_results", minimum: 1, maximum: 1000) ?? DefaultLimit;
        if (!context.TryResolve(arguments.OptionalString("path"), out var start, out var error))
        {
            return Task.FromResult(ToolResult.Error(error));
        }

        if (!Directory.Exists(start))
        {
            return Task.FromResult(ToolResult.Error($"The folder '{context.Relative(start)}' does not exist."));
        }

        Regex? matcher;
        try
        {
            matcher = string.IsNullOrEmpty(pattern) ? null : Glob.ForTool(pattern);
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"The pattern is not valid: {ex.Message}"));
        }

        var lines = new StringBuilder();
        var count = 0;
        var truncated = false;
        foreach (var entry in WorkspaceWalker.Walk(context, start, recursive, !includeIgnored, cancellationToken))
        {
            if (matcher is not null && !matcher.IsMatch(Path.GetRelativePath(start, entry.FullPath).Replace('\\', '/')))
            {
                continue;
            }

            if (count == limit)
            {
                truncated = true;
                break;
            }

            lines.Append(entry.RelativePath).Append(entry.IsDirectory ? "/" : string.Empty).Append('\n');
            count++;
        }

        if (count == 0)
        {
            return Task.FromResult(ToolResult.Ok(matcher is null ? "[The folder is empty.]" : "[Nothing matches the pattern.]"));
        }

        if (truncated)
        {
            lines.Append(string.Create(CultureInfo.InvariantCulture, $"[Showing the first {limit} entries. Use a narrower path or pattern to see the rest.]"));
        }

        return Task.FromResult(ToolResult.Ok(lines.ToString().TrimEnd('\n')));
    }
}
