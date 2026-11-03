using BlazeHarness.Core.Agent.Questions;

namespace BlazeHarness.Core.Tests.Unit.Agent;

/// <summary>A user who answers the questions a test scripts, one per call, and records what was asked.</summary>
internal sealed class FakeUserAsker : IUserAsker
{
    private readonly Func<string, UserQuestion, CancellationToken, Task<UserAnswer>> _answer;

    public FakeUserAsker(Func<string, UserQuestion, CancellationToken, Task<UserAnswer>> answer)
    {
        _answer = answer;
    }

    /// <summary>Gets the questions that were asked, in order.</summary>
    public List<(string CallId, UserQuestion Question)> Asked { get; } = [];

    /// <summary>Create a user who gives the same answer every time.</summary>
    /// <param name="answer">The answer to give.</param>
    /// <returns>The fake user.</returns>
    public static FakeUserAsker Always(UserAnswer answer) => new((_, _, _) => Task.FromResult(answer));

    public Task<UserAnswer> AskAsync(string callId, UserQuestion question, CancellationToken cancellationToken)
    {
        Asked.Add((callId, question));
        return _answer(callId, question, cancellationToken);
    }
}
