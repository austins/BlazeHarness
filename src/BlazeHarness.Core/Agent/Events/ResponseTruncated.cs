namespace BlazeHarness.Core.Agent.Events;

/// <summary>
/// The server stopped a response because it reached the output limit, so the text that was streamed is not complete.
/// </summary>
internal sealed record ResponseTruncated : AgentEvent;
