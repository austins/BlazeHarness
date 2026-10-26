namespace BlazeHarness.Core.Permissions;

/// <summary>
/// The outcome of a permission check.
/// </summary>
public enum PermissionVerdict
{
    /// <summary>The call runs.</summary>
    Allow,

    /// <summary>The call runs only if the user approves it.</summary>
    Ask,

    /// <summary>The call does not run.</summary>
    Deny,
}
