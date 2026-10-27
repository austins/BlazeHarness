namespace BlazeHarness.Core.Agent;

/// <summary>
/// A new assistant message begins because the model is being called again with the results of its tool calls.
/// </summary>
public sealed record NewResponse : AgentEvent;
