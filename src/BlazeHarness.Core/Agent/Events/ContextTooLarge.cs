namespace BlazeHarness.Core.Agent.Events;

/// <summary>
/// The run stopped before a request because compaction failed and the conversation no longer fits the model's context.
/// </summary>
internal sealed record ContextTooLarge : AgentEvent;
