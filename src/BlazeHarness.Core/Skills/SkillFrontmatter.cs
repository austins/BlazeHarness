using System.Text;
using System.Text.RegularExpressions;

namespace BlazeHarness.Core.Skills;

/// <summary>
/// Reads the YAML front matter at the top of a <c>SKILL.md</c> file.
/// </summary>
/// <remarks>
/// Only what skills use is understood: <c>key: value</c> lines at the top level, quoted values, values that continue on
/// indented lines, and the <c>&gt;</c> and <c>|</c> block forms. Fields other than the name, the description and <c>disable-model-invocation</c> are skipped.
/// </remarks>
internal static partial class SkillFrontmatter
{
    private const string Fence = "---";

    /// <summary>
    /// Split the text of a <c>SKILL.md</c> file into its fields and its instructions.
    /// </summary>
    /// <param name="text">The whole file.</param>
    /// <returns>The fields and the body, or an error when the front matter is missing or never closed.</returns>
    public static SkillFile Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = text.TrimStart('\uFEFF').ReplaceLineEndings("\n").Split('\n');
        if (lines.Length == 0 || lines[0].TrimEnd() != Fence)
        {
            return new SkillFile(null, null, text, "The file does not start with a --- front matter block.");
        }

        var end = Array.FindIndex(lines, 1, line => line.TrimEnd() == Fence);
        if (end < 0)
        {
            return new SkillFile(null, null, text, "The front matter block is not closed with ---.");
        }

        var fields = Fields(lines.Skip(1).Take(end - 1));
        var body = string.Join('\n', lines.Skip(end + 1)).Trim('\n');
        var disabled = string.Equals(Value(fields, "disable-model-invocation"), "true", StringComparison.OrdinalIgnoreCase);
        return new SkillFile(Value(fields, "name"), Value(fields, "description"), body, null, disabled);
    }

    private static List<(string Key, string Inline, List<string> More)> Fields(IEnumerable<string> lines)
    {
        List<(string Key, string Inline, List<string> More)> fields = [];
        foreach (var line in lines)
        {
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && TopLevelKey().Match(line) is { Success: true } match)
            {
                fields.Add((match.Groups[1].Value, match.Groups[2].Value.Trim(), []));
            }
            else if (fields.Count > 0 && line.Trim().Length > 0)
            {
                fields[^1].More.Add(line.Trim());
            }
        }

        return fields;
    }

    private static string? Value(List<(string Key, string Inline, List<string> More)> fields, string key)
    {
        if (fields.FindLast(f => f.Key == key) is not { Key: not null } field)
        {
            return null;
        }

        var (_, inline, more) = field;
        var text = inline switch
        {
            ">" or ">-" or ">+" or "|" or "|-" or "|+" => string.Join(' ', more),
            _ => string.Join(' ', more.Prepend(Unquote(inline)).Where(part => part.Length > 0)),
        };

        var flat = Whitespace().Replace(text, " ").Trim();
        return flat.Length == 0 ? null : flat;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            var inner = new StringBuilder();
            var escaped = false;
            foreach (var c in value[1..^1])
            {
                if (c == '\\' && !escaped)
                {
                    escaped = true;
                    continue;
                }

                escaped = false;
                inner.Append(c);
            }

            return inner.ToString();
        }

        return value.Length >= 2 && value[0] == '\'' && value[^1] == '\''
            ? value[1..^1].Replace("''", "'", StringComparison.Ordinal)
            : value;
    }

    [GeneratedRegex(@"^([A-Za-z0-9_-]+)\s*:\s*(.*)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TopLevelKey();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Whitespace();
}
