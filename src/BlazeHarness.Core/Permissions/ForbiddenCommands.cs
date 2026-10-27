using BlazeHarness.Core.Config;
using BlazeHarness.Core.Sandbox;
using System.Text.Json;

namespace BlazeHarness.Core.Permissions;

/// <summary>
/// The list of commands that need the user's explicit approval each time, such as recursive deletes, disk tools and privilege escalation.
/// </summary>
/// <remarks>
/// The rules are data in <c>forbidden-commands.json</c>, not code. Each shell command line is split into its simple commands, including
/// pipelines, substitutions and nested shells, and every one is checked. Text that is built at run time can't be checked, so it is flagged too.
/// This is a speed bump for the user's attention that sits on top of the OS sandbox, which is the actual boundary.
/// </remarks>
public sealed class ForbiddenCommands
{
    private static readonly Lazy<ForbiddenCommands> BuiltInList = new(LoadBuiltIn);

    private readonly List<ForbiddenRule> _rules;

    private ForbiddenCommands(IEnumerable<ForbiddenRule> rules)
    {
        _rules = [.. rules];
    }

    /// <summary>
    /// Gets a list that flags only commands that can't be checked.
    /// </summary>
    public static ForbiddenCommands None { get; } = new([]);

    /// <summary>
    /// Gets the list that ships with the app, for the operating system it runs on.
    /// </summary>
    public static ForbiddenCommands BuiltIn => BuiltInList.Value;

    /// <summary>
    /// Check a shell command line.
    /// </summary>
    /// <param name="command">The command text the model passed to the shell tool.</param>
    /// <returns>What is risky about it, or null when no part of it is on the list.</returns>
    public ForbiddenMatch? Find(string command)
    {
        var text = command.Replace('\\', '/');
        foreach (var segment in ShellCommandParser.Parse(command))
        {
            if (segment.Opaque is { } reason)
            {
                return new ForbiddenMatch(reason);
            }

            var rule = _rules.Find(r => Matches(r, segment, text));
            if (rule is not null)
            {
                return new ForbiddenMatch(rule.Risk);
            }
        }

        return null;
    }

    internal static ForbiddenCommands FromJson(string json, SandboxPlatform platform)
    {
        var document = JsonSerializer.Deserialize<RuleFile>(json, JsonDefaults.Options) ?? new RuleFile([]);
        return new ForbiddenCommands(document.Rules.Where(r => IsUsable(r, platform)));
    }

    private static ForbiddenCommands LoadBuiltIn()
    {
        using var stream = typeof(ForbiddenCommands).Assembly.GetManifestResourceStream("forbidden-commands.json")
            ?? throw new InvalidOperationException("The built-in forbidden command list is missing from the app.");
        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd(), SandboxPolicy.CurrentPlatform());
    }

    private static bool IsUsable(ForbiddenRule rule, SandboxPlatform platform)
    {
        return (rule.Programs.Count > 0 || rule.Contains.Count > 0)
            && (rule.Platforms.Count == 0 || rule.Platforms.Contains(platform));
    }

    private static bool Matches(ForbiddenRule rule, CommandSegment segment, string text)
    {
        return (rule.Programs.Count == 0 || rule.Programs.Any(p => ProgramMatches(p, segment.Program)))
            && StartsWithArguments(rule, segment)
            && (rule.Flags.Count == 0 || rule.Flags.Any(f => segment.Arguments.Any(a => FlagMatches(rule.FlagStyle, f, a))))
            && ContainsText(rule, segment, text);
    }

    private static bool ContainsText(ForbiddenRule rule, CommandSegment segment, string text)
    {
        if (rule.Contains.Count == 0)
        {
            return true;
        }

        // A rule for any program looks at the whole line, because the parser drops backslashes that the shell would treat as escapes but Windows uses in paths.
        return rule.Programs.Count == 0
            ? rule.Contains.Any(c => text.Contains(c, StringComparison.OrdinalIgnoreCase))
            : rule.Contains.Any(c => segment.Arguments.Any(a => a.Replace('\\', '/').Contains(c, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool ProgramMatches(string pattern, string program)
    {
        return pattern.EndsWith('*')
            ? program.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(pattern, program, StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsWithArguments(ForbiddenRule rule, CommandSegment segment)
    {
        return segment.Arguments.Count >= rule.Arguments.Count
            && rule.Arguments.Select((expected, i) => string.Equals(expected, segment.Arguments[i], StringComparison.OrdinalIgnoreCase)).All(same => same);
    }

    private static bool FlagMatches(FlagStyle style, string flag, string argument)
    {
        switch (style)
        {
            case FlagStyle.Windows:
                return argument.StartsWith('/') && argument[1..].Split('/').Any(part => string.Equals(part, flag[1..], StringComparison.OrdinalIgnoreCase));
            case FlagStyle.PowerShell:
                var name = argument.Split(':')[0];
                return name.Length >= 2 && name[0] == '-' && flag.StartsWith(name, StringComparison.OrdinalIgnoreCase);
            default:
                if (flag.Length == 2)
                {
                    return argument.Length >= 2 && argument[0] == '-' && argument[1] != '-' && argument.Contains(flag[1], StringComparison.Ordinal);
                }

                return argument == flag || argument.StartsWith(flag + "=", StringComparison.Ordinal);
        }
    }

    private sealed record RuleFile(IReadOnlyList<ForbiddenRule> Rules);
}
