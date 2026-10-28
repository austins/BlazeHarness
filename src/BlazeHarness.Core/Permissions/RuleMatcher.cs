using BlazeHarness.Core.Tools;
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace BlazeHarness.Core.Permissions;

/// <summary>
/// Finds which of the user's rules apply to a tool call.
/// </summary>
internal static class RuleMatcher
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly ConcurrentDictionary<(string Pattern, bool PathStyle), Regex> Compiled = new();

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
                hit = allKnown && pieces.TrueForAll(piece => Matches(rule, regex, piece));
            }
            else
            {
                hit = Matches(rule, regex, text) || pieces.Exists(piece => Matches(rule, regex, piece));
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
                if (candidates.Exists(candidate => Matches(rule, regex, candidate)))
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
        foreach (var rule in rules.Where(r => r.Target == RuleTarget.McpTool && r.Pattern.Length > 0 && Matches(r, Compile(r.Pattern, pathStyle: false), target)))
        {
            decided = Stronger(decided, rule);
        }

        return decided;
    }

    /// <summary>
    /// Find the rule that decides a web tool call.
    /// </summary>
    /// <param name="rules">The rules to search.</param>
    /// <param name="target">The host being fetched, or <c>web_search</c>.</param>
    /// <returns>The deciding rule, or null when none applies.</returns>
    public static PermissionRule? ForWeb(IReadOnlyList<PermissionRule> rules, string target)
    {
        // Host names are not case-sensitive, so both sides are compared in upper case.
        var text = target.ToUpperInvariant();
        PermissionRule? decided = null;
        foreach (var rule in rules.Where(r => r.Target == RuleTarget.WebHost && r.Pattern.Length > 0 && Matches(r, Compile(r.Pattern.ToUpperInvariant(), pathStyle: false), text)))
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

    private static bool Matches(PermissionRule rule, Regex regex, string text)
    {
        try
        {
            return regex.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            // Failing closed: a rule that can't be evaluated still refuses or asks, but never allows.
            return rule.Action != PermissionVerdict.Allow;
        }
    }

    private static Regex Compile(string pattern, bool pathStyle)
    {
        return Compiled.GetOrAdd((pattern, pathStyle), static key => Build(key.Pattern, key.PathStyle));
    }

    private static Regex Build(string pattern, bool pathStyle)
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
        // Linear-time matching, so a crafted command can't make a rule like *a*b*c* take exponential time.
        var options = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant | RegexOptions.Singleline | (OperatingSystem.IsWindows() && pathStyle ? RegexOptions.IgnoreCase : RegexOptions.None);
        return new Regex(builder.ToString(), options, RegexTimeout);
    }
}
