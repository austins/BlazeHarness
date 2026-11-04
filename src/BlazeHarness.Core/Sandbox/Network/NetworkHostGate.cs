using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Permissions;
using System.Collections.Concurrent;

namespace BlazeHarness.Core.Sandbox.Network;

/// <summary>
/// The network gate of one shell call: the user's rules and the chat's approvals decide, and what is left is asked in the chat.
/// </summary>
/// <remarks>
/// An answer to a question holds for the rest of the call, so a command that connects to the same host again is not asked again.
/// </remarks>
internal sealed class NetworkHostGate : INetworkGate
{
    private readonly PermissionEngine _permissions;
    private readonly IHostApprover? _approver;
    private readonly string _callId;
    private readonly ConcurrentDictionary<string, bool> _answered = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _refusals = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="NetworkHostGate"/> class.
    /// </summary>
    /// <param name="permissions">Decides from the user's rules and the chat's approvals.</param>
    /// <param name="approver">Asks the user, or null when nobody can be asked, which refuses what the rules don't allow.</param>
    /// <param name="callId">The id of the shell call, which the question is shown on.</param>
    public NetworkHostGate(PermissionEngine permissions, IHostApprover? approver, string callId)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentException.ThrowIfNullOrEmpty(callId);
        _permissions = permissions;
        _approver = approver;
        _callId = callId;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Refusals => [.. _refusals.Values.Order(StringComparer.Ordinal)];

    /// <inheritdoc />
    public async Task<NetworkAccess> RequestAsync(string host, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var decision = _permissions.EvaluateNetworkHost(host);
        switch (decision.Verdict)
        {
            case PermissionVerdict.Allow:
                return NetworkAccess.Allow;
            case PermissionVerdict.Deny:
                return Refuse(host, decision.Reason);
        }

        if (_answered.TryGetValue(host, out var allowed))
        {
            return allowed ? NetworkAccess.Allow : Refuse(host, $"The user refused {host} for this command.");
        }

        if (_approver is null)
        {
            return Refuse(host, $"{host} needs the user's approval, which can't be asked here.");
        }

        var choice = await _approver.RequestHostAsync(_callId, host, decision.GrantKey is not null, cancellationToken);
        _answered[host] = choice != ApprovalChoice.Deny;
        if (choice == ApprovalChoice.Deny)
        {
            return Refuse(host, $"The user refused {host}.");
        }

        if (choice == ApprovalChoice.AllowForChat && decision.GrantKey is { } key)
        {
            _permissions.Remember(key);
        }

        return NetworkAccess.Allow;
    }

    private NetworkAccess Refuse(string host, string reason)
    {
        _refusals[host] = reason;
        return NetworkAccess.Refuse(reason);
    }
}
