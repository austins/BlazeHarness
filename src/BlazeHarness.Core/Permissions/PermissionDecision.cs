using BlazeHarness.Core.Sandbox;

namespace BlazeHarness.Core.Permissions;

/// <summary>
/// What the permission check decided about one tool call.
/// </summary>
/// <param name="Verdict">Whether the call runs, needs the user's approval, or is refused.</param>
/// <param name="Reason">For <see cref="PermissionVerdict.Ask"/> what the user is asked to allow, and for <see cref="PermissionVerdict.Deny"/> what the model is told.</param>
/// <param name="GrantKey">For <see cref="PermissionVerdict.Ask"/> the key that remembers an approval for the rest of the session, or null when it can only be given once.</param>
/// <param name="AllowOutside">True when the call uses a path outside the working folder and must run with a context that accepts it.</param>
/// <param name="Shell">For a shell command, how far it may reach.</param>
/// <param name="ShellReadFolders">For a shell command, the folders outside the working folder it may also read, from earlier approvals.</param>
public sealed record PermissionDecision(
    PermissionVerdict Verdict,
    string Reason = "",
    string? GrantKey = null,
    bool AllowOutside = false,
    ShellMode Shell = ShellMode.Writable,
    IReadOnlyList<string>? ShellReadFolders = null)
{
    /// <summary>
    /// Gets a decision that lets the call run.
    /// </summary>
    public static PermissionDecision Allow { get; } = new(PermissionVerdict.Allow);
}
