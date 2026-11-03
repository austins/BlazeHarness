using BlazeHarness.Core.Agent.Questions;

namespace BlazeHarness.Core.Agent.Events;

/// <summary>
/// An <c>ask_user</c> call waits for the user to answer.
/// </summary>
/// <param name="CallId">The identifier of the call, matching <see cref="ToolCallRequested.CallId"/>.</param>
/// <param name="Question">The question to show.</param>
internal sealed record ToolAwaitingAnswer(string CallId, UserQuestion Question) : AgentEvent;
