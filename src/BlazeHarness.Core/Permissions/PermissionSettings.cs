namespace BlazeHarness.Core.Permissions;

/// <summary>
/// The two settings of a session that decide what its tool calls may do.
/// </summary>
/// <param name="Access">What the tools are allowed to do.</param>
/// <param name="Approvals">When the user is asked.</param>
public readonly record struct PermissionSettings(AccessLevel Access, ApprovalMode Approvals)
{
    /// <summary>
    /// Gets the settings a new session starts with.
    /// </summary>
    public static PermissionSettings Default { get; } = new(AccessLevel.WriteInFolder, ApprovalMode.AskFirst);

    /// <summary>
    /// Get the settings a new session starts with.
    /// </summary>
    /// <param name="hasFolder">True when the session works in a folder the user opened, and false for a chat that only has its own files.</param>
    /// <returns>The settings. A chat without a folder starts <see cref="AccessLevel.ReadOnly"/>, because nobody chose a place for it to change.</returns>
    public static PermissionSettings DefaultFor(bool hasFolder)
    {
        return hasFolder ? Default : Default with { Access = AccessLevel.ReadOnly };
    }

    /// <summary>
    /// Gets a value indicating whether these settings switch off a safeguard, so the user must confirm them and the UI keeps warning about them.
    /// </summary>
    public bool IsRisky => Access == AccessLevel.Unrestricted || Approvals == ApprovalMode.NeverAsk;
}
