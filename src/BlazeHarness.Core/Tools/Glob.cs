using System.Text;
using System.Text.RegularExpressions;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// Matches paths against glob patterns: <c>*</c> and <c>?</c> stay inside one path part, <c>**</c> spans parts, and <c>[abc]</c> is a set.
/// </summary>
internal static class Glob
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private static readonly bool IgnoreCase = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    /// <summary>
    /// Build a matcher for paths written with forward slashes.
    /// </summary>
    /// <param name="pattern">The glob pattern, with <c>\</c> escaping the next character.</param>
    /// <param name="anchored">
    /// True to match from the start of the path. False to also match at any depth, so <c>*.cs</c> finds files in every folder.
    /// </param>
    /// <returns>The regular expression.</returns>
    public static Regex Compile(string pattern, bool anchored)
    {
        var regex = new StringBuilder(anchored ? "^" : "^(?:.*/)?");
        var i = 0;
        while (i < pattern.Length)
        {
            var c = pattern[i++];
            switch (c)
            {
                case '*' when i < pattern.Length && pattern[i] == '*':
                    while (i < pattern.Length && pattern[i] == '*')
                    {
                        i++;
                    }

                    if (i < pattern.Length && pattern[i] == '/')
                    {
                        i++;
                        regex.Append("(?:.*/)?");
                    }
                    else
                    {
                        regex.Append(".*");
                    }

                    break;
                case '*':
                    regex.Append("[^/]*");
                    break;
                case '?':
                    regex.Append("[^/]");
                    break;
                case '[':
                    var end = i < pattern.Length ? pattern.IndexOf(']', i) : -1;
                    if (end <= i)
                    {
                        regex.Append(@"\[");
                        break;
                    }

                    var set = pattern[i..end];
                    regex.Append('[').Append(set.StartsWith('!') ? "^" + Regex.Escape(set[1..]) : Regex.Escape(set)).Append(']');
                    i = end + 1;
                    break;
                case '\\' when i < pattern.Length:
                    regex.Append(Regex.Escape(pattern[i++].ToString()));
                    break;
                default:
                    regex.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }

        regex.Append('$');
        var options = RegexOptions.CultureInvariant | (IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        return new Regex(regex.ToString(), options, MatchTimeout);
    }

    /// <summary>
    /// Build a matcher for a pattern from a tool call, which matches at any depth unless it names a folder.
    /// </summary>
    /// <param name="pattern">The glob pattern.</param>
    /// <returns>The regular expression.</returns>
    public static Regex ForTool(string pattern)
    {
        var trimmed = pattern.StartsWith("./", StringComparison.Ordinal) ? pattern[2..] : pattern;
        return Compile(trimmed, anchored: trimmed.Contains('/', StringComparison.Ordinal));
    }
}
