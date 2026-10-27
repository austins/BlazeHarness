using BlazeHarness.Core.Tools;
using System.Text.RegularExpressions;

namespace BlazeHarness.Core.Skills;

/// <summary>
/// The skills found in one folder.
/// </summary>
/// <param name="Skills">The skills that could be read, in name order.</param>
/// <param name="Problems">One message for each skill folder that was skipped, naming the folder and why.</param>
public sealed record SkillScan(IReadOnlyList<SkillInfo> Skills, IReadOnlyList<string> Problems)
{
    /// <summary>
    /// Gets a scan that found nothing.
    /// </summary>
    public static SkillScan Empty { get; } = new([], []);
}

/// <summary>
/// Finds the skills in a folder, where each skill is a subfolder with a <c>SKILL.md</c> file.
/// </summary>
public static partial class SkillScanner
{
    /// <summary>
    /// The longest a skill name may be.
    /// </summary>
    public const int MaxNameLength = 64;

    /// <summary>
    /// The longest description kept for a skill.
    /// </summary>
    public const int MaxDescriptionLength = 1024;

    /// <summary>
    /// The most skills read from one folder.
    /// </summary>
    public const int MaxSkills = 200;

    /// <summary>
    /// The largest <c>SKILL.md</c> file that is read, in bytes.
    /// </summary>
    public const long MaxFileBytes = 256 * 1024;

    /// <summary>
    /// Read the skills in a folder.
    /// </summary>
    /// <param name="directory">The folder to look in, such as <c>~/.agents/skills</c>. It does not need to exist.</param>
    /// <param name="source">Where the folder comes from.</param>
    /// <param name="boundary">When set, a link inside the folder that leads outside this path is skipped. Use it for folders that belong to a project, which may hold links to anywhere.</param>
    /// <returns>The skills and the problems found.</returns>
    public static SkillScan Scan(string directory, SkillSource source, string? boundary = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            return SkillScan.Empty;
        }

        List<SkillInfo> skills = [];
        List<string> problems = [];
        try
        {
            foreach (var folder in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
            {
                if (skills.Count >= MaxSkills)
                {
                    problems.Add($"{directory}: only the first {MaxSkills} skills are read.");
                    break;
                }

                var file = Path.Combine(folder, SkillInfo.FileName);
                if (!File.Exists(file))
                {
                    continue;
                }

                if (boundary is not null && (LeavesBoundary(folder, boundary) || LeavesBoundary(file, boundary)))
                {
                    problems.Add($"{Path.GetFileName(folder)}: skipped because it links outside the project folder.");
                    continue;
                }

                var skill = Read(folder, file, source, out var problem);
                if (skill is not null)
                {
                    skills.Add(skill);
                }
                else if (problem is not null)
                {
                    problems.Add($"{Path.GetFileName(folder)}: {problem}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"{directory}: {ex.Message}");
        }

        return new SkillScan([.. skills.OrderBy(s => s.Name, StringComparer.Ordinal)], problems);
    }

    /// <summary>
    /// Get whether a name is allowed: lowercase letters and digits, with single hyphens between them.
    /// </summary>
    /// <param name="name">The name to check.</param>
    /// <returns>True when the name is valid.</returns>
    public static bool IsValidName(string? name)
    {
        return name is { Length: > 0 and <= MaxNameLength } && ValidName().IsMatch(name);
    }

    private static SkillInfo? Read(string folder, string file, SkillSource source, out string? problem)
    {
        problem = null;
        string text;
        try
        {
            if (new FileInfo(file).Length > MaxFileBytes)
            {
                problem = "SKILL.md is larger than 256 KB.";
                return null;
            }

            text = File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = ex.Message;
            return null;
        }

        var parsed = SkillFrontmatter.Parse(text);
        if (parsed.Error is not null)
        {
            problem = parsed.Error;
            return null;
        }

        var name = parsed.Name ?? Path.GetFileName(folder);
        if (!IsValidName(name))
        {
            problem = $"'{name}' is not a valid name. Use lowercase letters, digits and single hyphens, up to {MaxNameLength} characters.";
            return null;
        }

        if (parsed.Description is not { } description)
        {
            problem = "The front matter has no description.";
            return null;
        }

        if (description.Length > MaxDescriptionLength)
        {
            description = description[..(MaxDescriptionLength - 1)] + "…";
        }

        return new SkillInfo(name, description, folder, source, !parsed.DisableModelInvocation);
    }

    private static bool LeavesBoundary(string path, string boundary)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is null && (info.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                return false;
            }

            var target = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
            return target is null || !WorkspacePath.IsInside(WorkspacePath.CanonicalRoot(boundary), target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ValidName();
}
