namespace BlazeHarness.Core.Agent;

/// <summary>
/// A tool call ended, whether it succeeded, failed, was denied or was cancelled.
/// </summary>
/// <param name="CallId">The identifier of the call, matching <see cref="ToolCallRequested.CallId"/>.</param>
/// <param name="Name">The name of the tool that was called.</param>
/// <param name="Output">The text of the result, or the reason the call did not run.</param>
/// <param name="Status">How the call ended.</param>
public sealed record ToolCallFinished(string CallId, string Name, string Output, ToolCallStatus Status) : AgentEvent;
