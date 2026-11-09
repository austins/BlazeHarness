using BlazeHarness.Core.Tools;
using BlazeHarness.Core.Tools.Files;
using System.Globalization;
using System.IO.Enumeration;
using System.Text;
using System.Text.Json;

namespace BlazeHarness.Core.Skills;

/// <summary>
/// The <c>load_skill</c> tool: returns a skill's instructions, or one of the files that come with it.
/// </summary>
internal sealed class LoadSkillTool : ITool
{
    /// <summary>
    /// The name the model calls the tool by.
    /// </summary>
    public const string ToolName = "load_skill";

    private const int MaxListedFiles = 50;

    /// <summary>
    /// The room kept under <see cref="ToolSet.MaxOutputCharacters"/> for the note that says how to read the next page.
    /// </summary>
    private const int FooterRoom = 300;

    private readonly SkillSet _skills;
    private readonly IReadOnlySet<string> _started;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoadSkillTool"/> class.
    /// </summary>
    /// <param name="skills">The skills the tool can load.</param>
    /// <param name="started">The names of skills the user started with <c>/name</c>, which the model may load even when they disable model invocation.</param>
    public LoadSkillTool(SkillSet skills, IReadOnlySet<string>? started = null)
    {
        ArgumentNullException.ThrowIfNull(skills);
        _skills = skills;
        _started = started ?? new HashSet<string>();
    }

    /// <inheritdoc />
    public string Name => ToolName;

    /// <inheritdoc />
    public string Description =>
        "Load a skill by name. Returns its instructions and the list of files that come with it. " +
        "Long instructions come in pages; the result says which offset reads the next one. " +
        "To read one of the skill's files, call again with the skill name and the file's path relative to the skill folder.";

    /// <inheritdoc />
    public string InputSchema => $$"""
        {
          "type": "object",
          "properties": {
            "name": { "type": "string", "description": "The skill's name, as listed in the system prompt." },
            "path": { "type": "string", "description": "A file inside the skill folder to read instead of the instructions." },
            "offset": { "type": "integer", "minimum": 1, "description": "The first line to read, starting at 1: of the file when path is set, otherwise of the instructions." },
            "limit": { "type": "integer", "minimum": 1, "maximum": {{ReadFileTool.MaxLimit}}, "description": "For a file: the most lines to read." }
          },
          "required": ["name"]
        }
        """;

    /// <inheritdoc />
    public bool IsReadOnly => true;

    /// <inheritdoc />
    public TimeSpan Timeout { get; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public string Summarize(JsonElement arguments)
    {
        var name = ToolSupport.Text(arguments, "name");
        var path = ToolSupport.Text(arguments, "path");
        return path.Length == 0 ? name : $"{name}/{path}";
    }

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var name = arguments.RequiredString("name");
        var path = arguments.OptionalString("path");
        var offset = arguments.OptionalInt("offset", minimum: 1) ?? 1;
        var limit = arguments.OptionalInt("limit", minimum: 1, maximum: ReadFileTool.MaxLimit) ?? ReadFileTool.DefaultLimit;
        var loadable = _skills.LoadableBy(_started);
        if (!loadable.Contains(name) || _skills.Skills.FirstOrDefault(skill => skill.Name == name) is not { } skill)
        {
            var known = string.Join(", ", _skills.Skills.Where(s => s.ModelInvocable).Select(s => s.Name));
            return Task.FromResult(ToolResult.Error($"There is no skill named '{name}'. The skills are: {(known.Length == 0 ? "none" : known)}."));
        }

        try
        {
            return Task.FromResult(string.IsNullOrWhiteSpace(path)
                ? Instructions(skill, offset)
                : LoadFile(skill, path, offset, limit, cancellationToken));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(ToolResult.Error($"Could not read the skill '{name}': {ex.Message}"));
        }
    }

    /// <summary>
    /// Read a page of a skill's instructions, which fits the output limit of a tool result.
    /// </summary>
    /// <remarks>
    /// The first page starts with the skill's folder and files. A page holds whole lines, except a single line too long for a page,
    /// and ends with the offset of the next page when there is more.
    /// </remarks>
    /// <param name="skill">The skill.</param>
    /// <param name="offset">The first line of the instructions to read, starting at 1.</param>
    /// <returns>The page, or an error when the file can't be used.</returns>
    /// <exception cref="IOException">The file could not be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The file may not be read.</exception>
    internal static ToolResult Instructions(SkillInfo skill, int offset = 1)
    {
        // The file may have grown since it was scanned.
        if (new FileInfo(skill.FilePath).Length > SkillScanner.MaxFileBytes)
        {
            return ToolResult.Error($"The instructions of '{skill.Name}' are larger than {SkillScanner.MaxFileBytes / 1024} KB.");
        }

        var text = File.ReadAllText(skill.FilePath);
        var parsed = SkillFrontmatter.Parse(text);
        var lines = (parsed.Error is null ? parsed.Body : text).ReplaceLineEndings("\n").Split('\n');
        if (offset > lines.Length)
        {
            return ToolResult.Error($"The instructions of '{skill.Name}' have {lines.Length} lines, so there is nothing at line {offset}.");
        }

        var result = new StringBuilder();
        if (offset == 1)
        {
            result.AppendLine(CultureInfo.InvariantCulture, $"Skill: {skill.Name}");
            result.AppendLine(CultureInfo.InvariantCulture, $"Folder: {skill.Directory}");
            result.AppendLine("Relative paths in the instructions are relative to that folder.");
            AppendFiles(result, skill);
            result.AppendLine();
        }
        else
        {
            result.AppendLine(CultureInfo.InvariantCulture, $"Skill: {skill.Name}, instructions from line {offset}");
            result.AppendLine();
        }

        var room = ToolSet.MaxOutputCharacters - FooterRoom - result.Length;
        var next = LinePages.Append(result, lines, offset, room);

        if (next < lines.Length)
        {
            result.AppendLine();
            result.AppendLine(CultureInfo.InvariantCulture, $"[Lines {offset}-{next} of {lines.Length}. Call {ToolName} with name \"{skill.Name}\" and offset {next + 1} to read the rest.]");
        }

        return ToolResult.Ok(result.ToString());
    }

    private static ToolResult LoadFile(SkillInfo skill, string path, int offset, int limit, CancellationToken cancellationToken)
    {
        if (!RootedPath.TryResolve(RootedPath.CanonicalRoot(skill.Directory), path, out var full, out var error))
        {
            return ToolResult.Error(error);
        }

        if (!File.Exists(full))
        {
            return ToolResult.Error($"The skill '{skill.Name}' has no file '{path}'.");
        }

        return ToolSupport.LooksBinary(full)
            ? ToolResult.Error($"'{path}' is a binary file, which can't be read as text.")
            : ReadFileTool.ReadLines(path, full, offset, limit, cancellationToken);
    }

    private static void AppendFiles(StringBuilder result, SkillInfo skill)
    {
        // Links are listed as files but never followed into, so a link loop cannot make the walk fail.
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 };
        var found = new FileSystemEnumerable<string>(skill.Directory, static (ref FileSystemEntry entry) => entry.ToFullPath(), options)
        {
            ShouldIncludePredicate = static (ref FileSystemEntry entry) => !entry.IsDirectory,
            ShouldRecursePredicate = static (ref FileSystemEntry entry) => !entry.Attributes.HasFlag(FileAttributes.ReparsePoint),
        };
        var files = found
            .Select(file => RootedPath.Relative(skill.Directory, file))
            .Where(file => !string.Equals(file, SkillInfo.FileName, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Take(MaxListedFiles + 1)
            .ToList();
        if (files.Count == 0)
        {
            return;
        }

        result.AppendLine("Files in the folder:");
        foreach (var file in files.Take(MaxListedFiles))
        {
            result.Append("- ").AppendLine(file);
        }

        if (files.Count > MaxListedFiles)
        {
            result.AppendLine("- (more files are not listed)");
        }
    }
}
