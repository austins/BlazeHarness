namespace BlazeHarness.Core.Agent;

/// <summary>
/// Asks the user whether a tool call may run.
/// </summary>
public interface IToolApprover
{
    /// <summary>
    /// Ask whether a call may run.
    /// </summary>
    /// <remarks>
    /// The agent loop calls this before it announces that the call awaits approval, so an implementation must
    /// register the request before its first <c>await</c>, or a fast answer could arrive before anything is listening.
    /// </remarks>
    /// <param name="callId">The provider's identifier for the call.</param>
    /// <param name="toolName">The name of the tool.</param>
    /// <param name="argumentsJson">The call arguments as a JSON object string.</param>
    /// <param name="cancellationToken">A token that is cancelled when the run stops.</param>
    /// <returns>True when the call may run, false when the user denied it.</returns>
    Task<bool> RequestAsync(string callId, string toolName, string argumentsJson, CancellationToken cancellationToken);
}
