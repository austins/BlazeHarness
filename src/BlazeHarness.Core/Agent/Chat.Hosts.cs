using BlazeHarness.Core.Permissions;
using System.Collections.Concurrent;

namespace BlazeHarness.Core.Agent;

public sealed partial class Chat : IHostApprover
{
    private readonly ConcurrentDictionary<(string CallId, string Host), TaskCompletionSource<ApprovalChoice>> _hostQuestions = new();

    /// <summary>
    /// Answer a question about a host that a running command wants to connect to.
    /// </summary>
    /// <param name="callId">The id of the shell call, from <see cref="ToolCallEntry.CallId"/>.</param>
    /// <param name="host">The host in <see cref="PendingHost.Host"/>.</param>
    /// <param name="choice">Whether the command may connect, and whether that holds for the rest of the chat.</param>
    /// <returns>True when the question was waiting; false when it had been answered or the call ended.</returns>
    public bool ApproveHost(string callId, string host, ApprovalChoice choice)
    {
        ArgumentNullException.ThrowIfNull(callId);
        ArgumentNullException.ThrowIfNull(host);
        return _hostQuestions.TryGetValue((callId, host), out var waiting) && waiting.TrySetResult(choice);
    }

    /// <inheritdoc />
    async Task<ApprovalChoice> IHostApprover.RequestHostAsync(string callId, string host, bool canRemember, CancellationToken cancellationToken)
    {
        var key = (callId, host);
        var waiting = _hostQuestions.GetOrAdd(key, static _ => new TaskCompletionSource<ApprovalChoice>(TaskCreationOptions.RunContinuationsAsynchronously));
        SetPendingHosts(callId, hosts => hosts.Any(pending => pending.Host == host) ? hosts : [.. hosts, new PendingHost(host, canRemember)]);
        using var registration = cancellationToken.Register(() => waiting.TrySetCanceled(cancellationToken));
        using var expiry = new CancellationTokenSource(ApprovalTimeout, _time);
        using var expiryRegistration = expiry.Token.Register(() => waiting.TrySetResult(ApprovalChoice.Deny));
        try
        {
            return await waiting.Task;
        }
        finally
        {
            _hostQuestions.TryRemove(key, out _);
            SetPendingHosts(callId, hosts => [.. hosts.Where(pending => pending.Host != host)]);
        }
    }

    private void SetPendingHosts(string callId, Func<IReadOnlyList<PendingHost>, IReadOnlyList<PendingHost>> change)
    {
        var call = Entries.SelectMany(entry => entry.ToolCalls).FirstOrDefault(c => c.CallId == callId);
        if (call is null)
        {
            return;
        }

        lock (_hostQuestions)
        {
            call.PendingHosts = change(call.PendingHosts);
        }

        RaiseChanged();
    }
}
