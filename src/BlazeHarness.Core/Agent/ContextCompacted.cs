namespace BlazeHarness.Core.Agent;

/// <summary>
/// Older messages were replaced by a summary, before the next model call.
/// </summary>
/// <param name="Summary">The summary that stands in for the messages before <paramref name="FirstKept"/>.</param>
/// <param name="FirstKept">The index, in the conversation without its system prompt, of the first message that is still sent as it was.</param>
/// <param name="TokensBefore">The estimated size of the conversation before compacting.</param>
/// <param name="TokensAfter">The estimated size of the conversation after compacting.</param>
public sealed record ContextCompacted(string Summary, int FirstKept, long TokensBefore, long TokensAfter) : AgentEvent;
