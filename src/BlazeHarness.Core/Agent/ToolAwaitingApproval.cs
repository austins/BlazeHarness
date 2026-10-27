namespace BlazeHarness.Core.Agent;

/// <summary>
/// A tool call waits for the user to allow or deny it.
/// </summary>
/// <param name="CallId">The identifier of the call, matching <see cref="ToolCallRequested.CallId"/>.</param>
/// <param name="Reason">What the user is asked to allow.</param>
/// <param name="CanRemember">True when the user may allow the same kind of call for the rest of the session.</param>
internal sealed record ToolAwaitingApproval(string CallId, string Reason, bool CanRemember) : AgentEvent;
