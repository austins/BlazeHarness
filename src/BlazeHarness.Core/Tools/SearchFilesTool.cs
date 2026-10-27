using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// Searches the text files inside the working folder for a regular expression.
/// </summary>
internal sealed class SearchFilesTool : ITool
{
    private const int DefaultLimit = 100;
    private const int MaxLineCharacters = 300;
    private const long MiB = 1024 * 1024;
    private const long MaxFileBytes = 2 * MiB;
    private static readonly TimeSpan DefaultRegexTimeout = TimeSpan.FromSeconds(2);

    private readonly TimeSpan _regexTimeout;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchFilesTool"/> class.
    /// </summary>
    /// <param name="regexTimeout">How long one pattern may take to match a line; two seconds when null.</param>
    public SearchFilesTool(TimeSpan? regexTimeout = null)
    {
        _regexTimeout = regexTimeout ?? DefaultRegexTimeout;
    }

    /// <inheritdoc />
    public string Name => "search_files";

    /// <inheritdoc />
    public string Description =>
        "Search the text files inside the working folder for a regular expression. Returns matching lines as path:line: text. " +
        $"Files that .gitignore excludes, binary files and files over {MaxFileBytes / MiB} MiB are skipped. Use glob to limit which files are searched.";

    /// <inheritdoc />
    public string InputSchema => """
        {
          "type": "object",
          "properties": {
            "pattern": { "type": "string", "description": "The regular expression to look for (.NET syntax)." },
            "path": { "type": "string", "description": "A folder or file to search, relative to the working folder. Defaults to the working folder." },
            "glob": { "type": "string", "description": "Only search files whose path matches this glob, for example '*.cs' or 'src/**/*.ts'." },
            "literal": { "type": "boolean", "description": "Treat pattern as plain text instead of a regular expression." },
            "ignore_case": { "type": "boolean", "description": "Match without regard to letter case." },
            "include_ignored": { "type": "boolean", "description": "Also search files that .gitignore excludes." },
            "max_results": { "type": "integer", "minimum": 1, "maximum": 500, "description": "The most matching lines to return. Defaults to 100." }
          },
          "required": ["pattern"]
        }
        """;

    /// <inheritdoc />
    public bool IsReadOnly => true;

    /// <inheritdoc />
    public IReadOnlyList<string> PathArguments { get; } = ["path"];

    /// <inheritdoc />
    public TimeSpan Timeout { get; } = TimeSpan.FromSeconds(60);

    /// <inheritdoc />
    public string Summarize(JsonElement arguments) => ToolSupport.Text(arguments, "pattern");

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(context);

        var pattern = arguments.RequiredString("pattern");
        var glob = arguments.OptionalString("glob");
        var limit = arguments.OptionalInt("max_results", minimum: 1, maximum: 500) ?? DefaultLimit;
        var options = RegexOptions.CultureInvariant | (arguments.OptionalBool("ignore_case") ? RegexOptions.IgnoreCase : RegexOptions.None);
        var includeIgnored = arguments.OptionalBool("include_ignored");
        if (pattern.Length == 0)
        {
            return Task.FromResult(ToolResult.Error("The pattern is empty."));
        }

        if (!context.TryResolve(arguments.OptionalString("path"), out var start, out var error))
        {
            return Task.FromResult(ToolResult.Error(error));
        }

        Regex search;
        Regex? files;
        try
        {
            search = new Regex(arguments.OptionalBool("literal") ? Regex.Escape(pattern) : pattern, options, _regexTimeout);
            files = string.IsNullOrEmpty(glob) ? null : Glob.ForTool(glob);
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Error($"The pattern is not valid: {ex.Message}"));
        }

        if (!Directory.Exists(start) && !File.Exists(start))
        {
            return Task.FromResult(ToolResult.Error($"'{context.Relative(start)}' does not exist."));
        }

        try
        {
            return Task.FromResult(Search(context, start, search, files, includeIgnored, limit, cancellationToken));
        }
        catch (RegexMatchTimeoutException)
        {
            return Task.FromResult(ToolResult.Error("The regular expression took too long on a line. Make it simpler or set literal to true."));
        }
    }

    private static ToolResult Search(ToolContext context, string start, Regex search, Regex? files, bool includeIgnored, int limit, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var matches = 0;
        var skippedLarge = 0;
        var truncated = false;

        var entries = File.Exists(start)
            ? [new WalkEntry(start, context.Relative(start), IsDirectory: false, IsLink: false)]
            : WorkspaceWalker.Walk(context, start, recursive: true, respectIgnore: !includeIgnored, cancellationToken);
        foreach (var entry in entries)
        {
            if (entry.IsDirectory || entry.IsLink
                || (files is not null && !files.IsMatch(Path.GetRelativePath(start, entry.FullPath).Replace('\\', '/'))))
            {
                continue;
            }

            if (!TryReadable(entry.FullPath, ref skippedLarge))
            {
                continue;
            }

            if (!SearchFile(entry, search, output, ref matches, limit, cancellationToken))
            {
                truncated = true;
                break;
            }
        }

        var text = matches == 0 ? "[No matches.]" : output.ToString().TrimEnd('\n');
        if (truncated)
        {
            text += string.Create(CultureInfo.InvariantCulture, $"\n[Showing the first {limit} matches. Use a narrower path, glob or pattern to see the rest.]");
        }

        if (skippedLarge > 0)
        {
            text += string.Create(CultureInfo.InvariantCulture, $"\n[Skipped files over 2 MiB: {skippedLarge}.]");
        }

        return ToolResult.Ok(text);
    }

    private static bool TryReadable(string path, ref int skippedLarge)
    {
        try
        {
            var length = new FileInfo(path).Length;
            if (length > MaxFileBytes)
            {
                skippedLarge++;
                return false;
            }

            return length > 0 && !ToolSupport.LooksBinary(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool SearchFile(WalkEntry entry, Regex search, StringBuilder output, ref int matches, int limit, CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(entry.FullPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var number = 0;
            while (reader.ReadLine() is { } line)
            {
                cancellationToken.ThrowIfCancellationRequested();
                number++;
                if (!search.IsMatch(line))
                {
                    continue;
                }

                if (matches == limit)
                {
                    return false;
                }

                var text = line.Trim();
                output.Append(entry.RelativePath).Append(':').Append(number).Append(": ")
                    .Append(text.Length > MaxLineCharacters ? text[..MaxLineCharacters] + " [line cut]" : text).Append('\n');
                matches++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file that can't be read is skipped.
        }

        return true;
    }
}
