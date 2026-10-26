namespace BlazeHarness.Core.Permissions;

/// <summary>
/// When the user is asked before a tool call runs.
/// </summary>
public enum ApprovalMode
{
    /// <summary>Ask for risky actions, and for anything outside the session's folder. This is the default.</summary>
    AskFirst,

    /// <summary>Ask only when the access level would otherwise block the action or the call reads outside the folder.</summary>
    AutoApprove,

    /// <summary>Never ask; calls that need approval run, and calls the access level forbids are refused.</summary>
    NeverAsk,
}
