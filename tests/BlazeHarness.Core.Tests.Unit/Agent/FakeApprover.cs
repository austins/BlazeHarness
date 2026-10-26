using BlazeHarness.Core.Agent;

namespace BlazeHarness.Core.Tests.Unit.Agent;

/// <summary>An approver that answers from a function and remembers what it was asked.</summary>
internal sealed class FakeApprover : IToolApprover
{
    private readonly Func<string, Task<bool>> _decide;

    public FakeApprover(bool allow)
        : this(_ => Task.FromResult(allow))
    {
    }

    public FakeApprover(Func<string, Task<bool>> decide)
    {
        _decide = decide;
    }

    /// <summary>Gets the names of the tools asked about.</summary>
    public List<string> Asked { get; } = [];

    public Task<bool> RequestAsync(string callId, string toolName, string argumentsJson, CancellationToken cancellationToken)
    {
        Asked.Add(toolName);
        return _decide(callId);
    }
}
