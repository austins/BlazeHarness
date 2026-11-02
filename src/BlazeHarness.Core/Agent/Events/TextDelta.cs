namespace BlazeHarness.Core.Agent.Events;

/// <summary>
/// A chunk of the assistant's visible reply.
/// </summary>
/// <param name="Text">The text to append to the reply being streamed.</param>
internal sealed record TextDelta(string Text) : AgentEvent;
