using BlazeHarness.Core.Permissions;

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
    /// <param name="request">The call and what the user is asked to allow.</param>
    /// <param name="cancellationToken">A token that is cancelled when the run stops.</param>
    /// <returns>How the user answered.</returns>
    Task<ApprovalChoice> RequestAsync(ApprovalRequest request, CancellationToken cancellationToken);
}
