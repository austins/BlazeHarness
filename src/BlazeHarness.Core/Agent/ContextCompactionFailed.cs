namespace BlazeHarness.Core.Agent;

/// <summary>
/// The conversation could not be compacted, so the run carries on with the full history.
/// </summary>
/// <param name="Message">Why compacting failed.</param>
internal sealed record ContextCompactionFailed(string Message) : AgentEvent;
