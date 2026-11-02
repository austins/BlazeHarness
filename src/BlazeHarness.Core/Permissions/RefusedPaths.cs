namespace BlazeHarness.Core.Permissions;

/// <summary>
/// The paths the user's file path rules refuse, for the tools that walk folders and so reach files no call named.
/// </summary>
public sealed class RefusedPaths
{
    private readonly PermissionRule[] _refusing;
    private readonly string _root;

    /// <summary>
    /// Initializes a new instance of the <see cref="RefusedPaths"/> class.
    /// </summary>
    /// <param name="rules">The user's rules. Only the file path rules that refuse are used.</param>
    /// <param name="root">The working folder, which relative patterns resolve against.</param>
    public RefusedPaths(IReadOnlyList<PermissionRule> rules, string root)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(root);
        _refusing = [.. rules.Where(r => r is { Target: RuleTarget.FilePath, Action: PermissionVerdict.Deny } && r.Pattern.Length > 0)];
        _root = root;
    }

    /// <summary>
    /// Gets a set that refuses nothing.
    /// </summary>
    public static RefusedPaths None { get; } = new([], string.Empty);

    /// <summary>
    /// Gets whether any rule refuses a path, so a caller can skip the work of checking when none does.
    /// </summary>
    public bool Any => _refusing.Length > 0;

    /// <summary>
    /// Gets the patterns of the refusing rules, so a walk can tell where they can match.
    /// </summary>
    internal IEnumerable<string> Patterns => _refusing.Select(rule => rule.Pattern);

    /// <summary>
    /// Gets the working folder that relative patterns resolve against.
    /// </summary>
    internal string Root => _root;

    /// <summary>
    /// Get whether a rule refuses a path.
    /// </summary>
    /// <param name="fullPath">The real full path of a file or folder.</param>
    /// <returns>True when a refusing rule matches the path.</returns>
    public bool IsRefused(string fullPath)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        return Any && RuleMatcher.ForPaths(_refusing, [fullPath], _root) is not null;
    }
}
