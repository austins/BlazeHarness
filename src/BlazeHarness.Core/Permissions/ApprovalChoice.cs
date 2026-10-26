namespace BlazeHarness.Core.Permissions;

/// <summary>
/// How the user answered an approval prompt.
/// </summary>
public enum ApprovalChoice
{
    /// <summary>The call does not run.</summary>
    Deny,

    /// <summary>The call runs this once.</summary>
    AllowOnce,

    /// <summary>The call runs, and the same kind of call runs without asking for the rest of the session.</summary>
    AllowForSession,
}
