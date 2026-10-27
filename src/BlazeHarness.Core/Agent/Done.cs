namespace BlazeHarness.Core.Agent;

/// <summary>
/// The final event of every run.
/// </summary>
/// <param name="Reason">Why the run ended.</param>
public sealed record Done(StopReason Reason) : AgentEvent;
