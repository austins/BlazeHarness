namespace BlazeHarness.Core.Agent;

/// <summary>
/// Why an agent run ended.
/// </summary>
public enum StopReason
{
    /// <summary>The model finished without requesting more tool calls.</summary>
    Completed,

    /// <summary>The user stopped the run.</summary>
    Cancelled,

    /// <summary>The run failed; an <see cref="AgentError"/> precedes this event.</summary>
    Error,
}
