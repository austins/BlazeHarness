namespace BlazeHarness.Core.Agent.Events;

/// <summary>
/// The loop added a user message to the history between two model calls.
/// </summary>
/// <remarks>
/// The message is already the last one in the history the loop was given. The consumer adds a matching chat entry.
/// </remarks>
/// <param name="Text">The message text.</param>
internal sealed record MessagesInjected(string Text) : AgentEvent;
