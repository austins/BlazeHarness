using BlazeHarness.Core.Tools;
using System.Text;
using System.Text.RegularExpressions;

namespace BlazeHarness.Core.Permissions;

/// <summary>
/// Finds which of the user's rules apply to a tool call.
/// </summary>
internal static class RuleMatcher
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Find the rule that decides a shell call.
    /// </summary>
    /// <param name="rules">The user's rules.</param>
    /// <param name="command">The command text.</param>
    /// <returns>The deciding rule, or null when none applies.</returns>
    public static PermissionRule? ForCommand(IReadOnlyList<PermissionRule> rules, string command)
    {
        var text = command.Trim();
        var segments = ShellCommandParser.Parse(command);
        var pieces = segments.Where(s => s.Opaque is null).Select(Describe).ToList();
        var allKnown = segments.Count > 0 && pieces.Count == segments.Count;

        PermissionRule? decided = null;
        foreach (var rule in rules.Where(r => r.Target == RuleTarget.ShellCommand && r.Pattern.Length > 0))
        {
            var regex = Compile(rule.Pattern, pathStyle: false);
            bool hit;
            if (rule.Action == PermissionVerdict.Allow)
            {
                hit = allKnown && pieces.TrueForAll(piece => Matches(regex, piece));
            }
            else
            {
                hit = Matches(regex, text) || pieces.Exists(piece => Matches(regex, piece));
            }

            if (hit)
            {
                decided = Stronger(decided, rule);
            }
        }

        return decided;
    }

    /// <summary>
    /// Find the rule that decides a file tool call.
    /// </summary>
    /// <param name="rules">The user's rules.</param>
    /// <param name="fullPaths">The real paths the call touches.</param>
    /// <param name="root">The working folder.</param>
    /// <returns>The deciding rule, or null when none applies.</returns>
    public static PermissionRule? ForPaths(IReadOnlyList<PermissionRule> rules, IReadOnlyList<string> fullPaths, string root)
    {
        PermissionRule? denied = null;
        PermissionRule? asked = null;
        PermissionRule? allowed = null;
        var allCovered = fullPaths.Count > 0;
        foreach (var path in fullPaths)
        {
            var candidates = Candidates(path, root);
            PermissionRule? decided = null;
            foreach (var rule in rules.Where(r => r.Target == RuleTarget.FilePath && r.Pattern.Length > 0))
            {
                var regex = Compile(rule.Pattern, pathStyle: true);
                if (candidates.Exists(candidate => Matches(regex, candidate)))
                {
                    decided = Stronger(decided, rule);
                }
            }

            denied ??= decided is { Action: PermissionVerdict.Deny } ? decided : null;
            asked ??= decided is { Action: PermissionVerdict.Ask } ? decided : null;
            allowed = decided is { Action: PermissionVerdict.Allow } ? decided : allowed;
            allCovered &= decided is { Action: PermissionVerdict.Allow };
        }

        return denied ?? asked ?? (allCovered ? allowed : null);
    }

    /// <summary>
    /// Find the rule that decides an MCP tool call.
    /// </summary>
    /// <param name="rules">The user's rules.</param>
    /// <param name="serverName">The name of the MCP server.</param>
    /// <param name="toolName">The tool's own name on that server.</param>
    /// <returns>The deciding rule, or null when none applies.</returns>
    public static PermissionRule? ForMcpTool(IReadOnlyList<PermissionRule> rules, string serverName, string toolName)
    {
        var target = serverName + "/" + toolName;
        PermissionRule? decided = null;
        foreach (var rule in rules.Where(r => r.Target == RuleTarget.McpTool && r.Pattern.Length > 0 && Matches(Compile(r.Pattern, pathStyle: false), target)))
        {
            decided = Stronger(decided, rule);
        }

        return decided;
    }

    private static PermissionRule Stronger(PermissionRule? current, PermissionRule next)
    {
        return current is { Action: PermissionVerdict.Deny } && next.Action != PermissionVerdict.Deny ? current : next;
    }

    private static string Describe(CommandSegment segment)
    {
        return segment.Arguments.Count == 0 ? segment.Program : segment.Program + " " + string.Join(' ', segment.Arguments);
    }

    private static List<string> Candidates(string fullPath, string root)
    {
        var full = fullPath.Replace('\\', '/');
        List<string> candidates = [full];
        if (WorkspacePath.IsInside(root, fullPath))
        {
            candidates.Add(WorkspacePath.Relative(root, fullPath));
        }

        return candidates;
    }

    private static bool Matches(Regex regex, string text)
    {
        try
        {
            return regex.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static Regex Compile(string pattern, bool pathStyle)
    {
        var text = pathStyle ? pattern.Replace('\\', '/') : pattern;
        var builder = new StringBuilder("^");
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            var step = 1;
            if (c == '*' && pathStyle && i + 1 < text.Length && text[i + 1] == '*')
            {
                var slash = i + 2 < text.Length && text[i + 2] == '/';
                builder.Append(slash ? "(?:.*/)?" : ".*");
                step = slash ? 3 : 2;
            }
            else if (c == '*')
            {
                builder.Append(pathStyle ? "[^/]*" : ".*");
            }
            else if (c == '?')
            {
                builder.Append(pathStyle ? "[^/]" : ".");
            }
            else
            {
                builder.Append(Regex.Escape(c.ToString()));
            }

            i += step;
        }

        builder.Append('$');
        var options = RegexOptions.CultureInvariant | RegexOptions.Singleline | (OperatingSystem.IsWindows() && pathStyle ? RegexOptions.IgnoreCase : RegexOptions.None);
        return new Regex(builder.ToString(), options, RegexTimeout);
    }
}
