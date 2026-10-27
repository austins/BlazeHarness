using System.Text.RegularExpressions;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// The rules of one <c>.gitignore</c> file.
/// </summary>
/// <remarks>
/// Supports comments, negation with <c>!</c>, folder-only rules ending in <c>/</c>, rules anchored by a slash,
/// and the glob syntax of <see cref="Glob"/>. The last rule that matches decides. A rule that is not valid is skipped.
/// </remarks>
internal sealed class GitIgnore
{
    private readonly List<Rule> _rules = [];

    private GitIgnore()
    {
    }

    /// <summary>
    /// Parse the text of a <c>.gitignore</c> file.
    /// </summary>
    /// <param name="text">The file content.</param>
    /// <returns>The rules.</returns>
    public static GitIgnore Parse(string text)
    {
        var result = new GitIgnore();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            line = TrimUnescapedTrailingSpaces(line);
            var negated = line.StartsWith('!');
            if (negated)
            {
                line = line[1..];
            }

            var directoryOnly = line.EndsWith('/');
            line = line.TrimEnd('/');
            if (line.StartsWith("\\#", StringComparison.Ordinal) || line.StartsWith("\\!", StringComparison.Ordinal))
            {
                line = line[1..];
            }

            if (line.Length == 0)
            {
                continue;
            }

            var anchored = line.Contains('/', StringComparison.Ordinal);
            try
            {
                result._rules.Add(new Rule(Glob.Compile(line.TrimStart('/'), anchored), negated, directoryOnly));
            }
            catch (ArgumentException)
            {
                // Git ignores a rule it can't read too, so one bad rule must not hide the rest.
            }
        }

        return result;
    }

    /// <summary>
    /// Read the <c>.gitignore</c> file in a folder.
    /// </summary>
    /// <param name="folder">The full path of the folder.</param>
    /// <returns>The rules, or null when the folder has no readable file.</returns>
    public static GitIgnore? Load(string folder)
    {
        var file = Path.Combine(folder, ".gitignore");
        try
        {
            return File.Exists(file) ? Parse(File.ReadAllText(file)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Check a path against the rules.
    /// </summary>
    /// <param name="relativePath">The path relative to the folder of the file, with forward slashes.</param>
    /// <param name="isDirectory">Whether the path is a folder.</param>
    /// <returns>True when ignored, false when a later rule brings it back, or null when no rule matches.</returns>
    public bool? Check(string relativePath, bool isDirectory)
    {
        bool? result = null;
        foreach (var rule in _rules)
        {
            if ((!rule.DirectoryOnly || isDirectory) && rule.Pattern.IsMatch(relativePath))
            {
                result = !rule.Negated;
            }
        }

        return result;
    }

    private static string TrimUnescapedTrailingSpaces(string line)
    {
        var end = line.Length;
        while (end > 0 && line[end - 1] == ' ' && (end < 2 || line[end - 2] != '\\'))
        {
            end--;
        }

        return line[..end];
    }

    private sealed record Rule(Regex Pattern, bool Negated, bool DirectoryOnly);
}
