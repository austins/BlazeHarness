using BlazeHarness.Core.Agent;

namespace BlazeHarness.Core.Tests.Unit.Agent;

/// <summary>A run inbox that hands out the messages a test scripts, one per call, and then nothing.</summary>
internal sealed class FakeRunInbox : IRunInbox
{
    private readonly Queue<string?> _answers;

    public FakeRunInbox(params string?[] answers)
    {
        _answers = new Queue<string?>(answers);
    }

    /// <summary>Gets how many times the runner asked for messages.</summary>
    public int Calls { get; private set; }

    public string? TakeMessages()
    {
        Calls++;
        return _answers.TryDequeue(out var answer) ? answer : null;
    }
}
