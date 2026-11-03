namespace BlazeHarness.Core.Agent;

/// <summary>
/// Where one item of the agent's to-do list stands.
/// </summary>
public enum TodoStatus
{
    /// <summary>The item has not been started.</summary>
    Pending,

    /// <summary>The item is being worked on now.</summary>
    InProgress,

    /// <summary>The item is finished.</summary>
    Completed,
}
