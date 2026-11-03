namespace BlazeHarness.Core.Agent;

/// <summary>
/// A message the user wrote during a reply that waits to be added to the conversation.
/// </summary>
/// <param name="Text">The message text.</param>
/// <param name="Mode">When the message is added.</param>
public sealed record QueuedMessage(string Text, QueueMode Mode);
