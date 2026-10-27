namespace BlazeHarness.Core.Agent;

/// <summary>
/// A chunk of the model's reasoning ("thinking") text.
/// </summary>
/// <param name="Text">The text to append to the reasoning being streamed.</param>
internal sealed record ReasoningDelta(string Text) : AgentEvent;
