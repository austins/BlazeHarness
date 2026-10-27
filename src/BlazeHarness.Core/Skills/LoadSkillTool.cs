using BlazeHarness.Core.Tools;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BlazeHarness.Core.Skills;

/// <summary>
/// The <c>load_skill</c> tool: returns a skill's instructions, or one of the files that come with it.
/// </summary>
public sealed class LoadSkillTool : ITool
{
    /// <summary>
    /// The name the model calls the tool by.
    /// </summary>
    public const string ToolName = "load_skill";

    private const int MaxBodyCharacters = 64 * 1024;
    private const int MaxListedFiles = 50;

    private readonly SkillSet _skills;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoadSkillTool"/> class.
    /// </summary>
    /// <param name="skills">The skills the tool can load.</param>
    public LoadSkillTool(SkillSet skills)
    {
        ArgumentNullException.ThrowIfNull(skills);
        _skills = skills;
    }

    /// <inheritdoc />
    public string Name => ToolName;

    /// <inheritdoc />
    public string Description =>
        "Load a skill by name. Returns its instructions and the list of files that come with it. " +
        "To read one of those files, call again with the skill name and the file's path relative to the skill folder.";

    /// <inheritdoc />
    public string InputSchema => """
        {
          "type": "object",
          "properties": {
            "name": { "type": "string", "description": "The skill's name, as listed in the system prompt." },
            "path": { "type": "string", "description": "A file inside the skill folder to read instead of the instructions." },
            "offset": { "type": "integer", "minimum": 1, "description": "For a file: the first line to read, starting at 1." },
            "limit": { "type": "integer", "minimum": 1, "maximum": 10000, "description": "For a file: the most lines to read." }
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
        var limit = arguments.OptionalInt("limit", minimum: 1, maximum: 10_000) ?? 2000;
        if (_skills.Skills.FirstOrDefault(skill => skill.Name == name) is not { } skill)
        {
            var known = string.Join(", ", _skills.Skills.Select(s => s.Name));
            return Task.FromResult(ToolResult.Error($"There is no skill named '{name}'. The skills are: {(known.Length == 0 ? "none" : known)}."));
        }

        try
        {
            return Task.FromResult(string.IsNullOrWhiteSpace(path)
                ? LoadInstructions(skill)
                : LoadFile(skill, path, offset, limit, cancellationToken));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(ToolResult.Error($"Could not read the skill '{name}': {ex.Message}"));
        }
    }

    private static ToolResult LoadInstructions(SkillInfo skill)
    {
        var parsed = SkillFrontmatter.Parse(File.ReadAllText(skill.FilePath));
        var body = parsed.Error is null ? parsed.Body : File.ReadAllText(skill.FilePath);
        var cut = body.Length > MaxBodyCharacters;
        var result = new StringBuilder();
        result.AppendLine(CultureInfo.InvariantCulture, $"Skill: {skill.Name}");
        result.AppendLine(CultureInfo.InvariantCulture, $"Folder: {skill.Directory}");
        result.AppendLine("Relative paths in the instructions are relative to that folder.");
        AppendFiles(result, skill);
        result.AppendLine();
        result.AppendLine(cut ? body[..MaxBodyCharacters] : body);
        if (cut)
        {
            result.AppendLine(CultureInfo.InvariantCulture, $"[The instructions were cut off after {MaxBodyCharacters:N0} characters.]");
        }

        return ToolResult.Ok(result.ToString());
    }

    private static ToolResult LoadFile(SkillInfo skill, string path, int offset, int limit, CancellationToken cancellationToken)
    {
        if (!WorkspacePath.TryResolve(skill.Directory, path, out var full, out var error))
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
        var files = Directory.EnumerateFiles(skill.Directory, "*", SearchOption.AllDirectories)
            .Select(file => WorkspacePath.Relative(skill.Directory, file))
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
