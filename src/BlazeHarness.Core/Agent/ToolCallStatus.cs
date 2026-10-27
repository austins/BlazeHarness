namespace BlazeHarness.Core.Agent;

/// <summary>
/// Where a tool call is in its life, as the UI shows it.
/// </summary>
public enum ToolCallStatus
{
    /// <summary>The model asked for the call and it has not started.</summary>
    Pending,

    /// <summary>The call is waiting for the user to allow or deny it.</summary>
    AwaitingApproval,

    /// <summary>The call is running.</summary>
    Running,

    /// <summary>The call ran and returned a result.</summary>
    Succeeded,

    /// <summary>The call could not run or returned an error.</summary>
    Failed,

    /// <summary>The user denied the call, so it did not run.</summary>
    Denied,

    /// <summary>The user stopped the run before the call finished.</summary>
    Cancelled,

    /// <summary>The app stopped before the call finished.</summary>
    Interrupted,
}
