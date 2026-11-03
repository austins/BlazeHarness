namespace BlazeHarness.Core.Agent.Events;

/// <summary>
/// Large tool output from older messages was replaced by a short stub in the request, before the next model call. No message was removed.
/// </summary>
/// <param name="TrimmedBefore">The index, in the conversation without its system prompt, of the first message whose tool output is still sent in full.</param>
/// <param name="TokensBefore">The estimated size of the conversation before trimming.</param>
/// <param name="TokensAfter">The estimated size of the conversation after trimming.</param>
public sealed record ContextTrimmed(int TrimmedBefore, long TokensBefore, long TokensAfter) : AgentEvent;
