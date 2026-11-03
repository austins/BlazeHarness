namespace BlazeHarness.Core.Agent;

/// <summary>
/// One step on the agent's to-do list.
/// </summary>
/// <param name="Text">What to do, on one line.</param>
/// <param name="Status">Where the step stands.</param>
public sealed record TodoItem(string Text, TodoStatus Status);
