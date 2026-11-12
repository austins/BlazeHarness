namespace BlazeHarness.Core.Agent.Events;

/// <summary>
/// Base type of everything an agent run emits.
/// </summary>
/// <remarks>
/// The UI (or a future CLI) only consumes this stream; it never calls into the agent loop's internals.
/// </remarks>
public closed record AgentEvent;
