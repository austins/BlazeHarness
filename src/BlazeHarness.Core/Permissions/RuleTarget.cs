namespace BlazeHarness.Core.Permissions;

/// <summary>
/// What a <see cref="PermissionRule"/> is matched against.
/// </summary>
public enum RuleTarget
{
    /// <summary>The command text of a shell call, or any simple command inside it.</summary>
    ShellCommand,

    /// <summary>A path a file tool reads or changes.</summary>
    FilePath,
}
