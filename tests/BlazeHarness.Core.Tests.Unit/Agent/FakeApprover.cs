using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Permissions;

namespace BlazeHarness.Core.Tests.Unit.Agent;

/// <summary>An approver that answers from a function and remembers what it was asked.</summary>
internal sealed class FakeApprover : IToolApprover
{
    private readonly Func<string, Task<ApprovalChoice>> _decide;

    public FakeApprover(bool allow)
        : this(_ => Task.FromResult(allow ? ApprovalChoice.AllowOnce : ApprovalChoice.Deny))
    {
    }

    public FakeApprover(Func<string, Task<ApprovalChoice>> decide)
    {
        _decide = decide;
    }

    /// <summary>Gets the names of the tools asked about.</summary>
    public List<string> Asked { get; } = [];

    /// <summary>Gets the requests the approver received.</summary>
    public List<ApprovalRequest> Requests { get; } = [];

    public Task<ApprovalChoice> RequestAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        Asked.Add(request.ToolName);
        Requests.Add(request);
        return _decide(request.CallId);
    }
}
