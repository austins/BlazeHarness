namespace BlazeHarness.Core.Agent;

/// <summary>
/// The run hit an error it could not recover from.
/// </summary>
/// <param name="Message">A user-readable description of the failure.</param>
internal sealed record AgentError(string Message) : AgentEvent;
