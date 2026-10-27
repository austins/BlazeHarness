namespace BlazeHarness.Core.Agent;

/// <summary>
/// A tool call started running.
/// </summary>
/// <param name="CallId">The identifier of the call, matching <see cref="ToolCallRequested.CallId"/>.</param>
public sealed record ToolCallRunning(string CallId) : AgentEvent;
