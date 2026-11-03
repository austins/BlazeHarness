namespace BlazeHarness.Core.Agent.Questions;

/// <summary>
/// Asks the user a question the model wrote and waits for the answer.
/// </summary>
internal interface IUserAsker
{
    /// <summary>
    /// Ask the user a question.
    /// </summary>
    /// <remarks>
    /// The agent loop calls this before it announces the question, so an implementation must register the question
    /// before its first <c>await</c>, or a fast answer could arrive before anything is listening. There is no time limit.
    /// </remarks>
    /// <param name="callId">The id of the <c>ask_user</c> call, which the answer is matched by.</param>
    /// <param name="question">The question.</param>
    /// <param name="cancellationToken">A token that is cancelled when the run stops.</param>
    /// <returns>How the user answered.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before the user answered.</exception>
    Task<UserAnswer> AskAsync(string callId, UserQuestion question, CancellationToken cancellationToken);
}
