namespace BlazeHarness.Core.Permissions;

/// <summary>
/// The two settings of a session that decide what its tool calls may do.
/// </summary>
/// <param name="Access">What the tools are allowed to do.</param>
/// <param name="Approvals">When the user is asked.</param>
public readonly record struct PermissionSettings(AccessLevel Access, ApprovalMode Approvals)
{
    /// <summary>
    /// Gets the built-in settings, which a session that stores none of its own uses.
    /// </summary>
    public static PermissionSettings Default { get; } = new(AccessLevel.WriteInFolder, ApprovalMode.AskFirst);

    /// <summary>
    /// Gets a value indicating whether these settings switch off a safeguard, so the user must confirm them and the UI keeps warning about them.
    /// </summary>
    public bool IsRisky => Access == AccessLevel.Unrestricted || Approvals == ApprovalMode.NeverAsk;
}
