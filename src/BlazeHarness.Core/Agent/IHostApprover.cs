using BlazeHarness.Core.Permissions;

namespace BlazeHarness.Core.Agent;

/// <summary>
/// Asks the user whether a command that is already running may connect to a host.
/// </summary>
/// <remarks>
/// Unlike <see cref="IToolApprover"/>, which is asked before a call starts, this is asked while the call runs, once for each host it reaches.
/// </remarks>
internal interface IHostApprover
{
    /// <summary>
    /// Ask whether a running call may connect to a host.
    /// </summary>
    /// <param name="callId">The provider's identifier for the call.</param>
    /// <param name="host">The host the call wants to connect to.</param>
    /// <param name="canRemember">True when <see cref="ApprovalChoice.AllowForChat"/> is offered.</param>
    /// <param name="cancellationToken">A token that is cancelled when the call ends.</param>
    /// <returns>How the user answered.</returns>
    Task<ApprovalChoice> RequestHostAsync(string callId, string host, bool canRemember, CancellationToken cancellationToken);
}
