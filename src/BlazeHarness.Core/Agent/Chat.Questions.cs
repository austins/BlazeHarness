using BlazeHarness.Core.Agent.Questions;
using BlazeHarness.Core.Permissions;
using System.Collections.Concurrent;

namespace BlazeHarness.Core.Agent;

public sealed partial class Chat
{
    private readonly ConcurrentDictionary<string, PendingQuestion> _questions = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets a value indicating whether an <c>ask_user</c> question waits for the user's answer.
    /// </summary>
    /// <remarks>
    /// A waiting question keeps <see cref="IsRunning"/> true and uses no tokens. It has no time limit, unlike an approval.
    /// </remarks>
    public bool IsWaitingForUser => !_questions.IsEmpty;

    /// <summary>
    /// Gets a value indicating whether the chat waits for the user, either to answer a question or to allow a tool call.
    /// </summary>
    public bool NeedsUser => Entries.Count > 0
        && Entries[^1].ToolCalls.Any(call => call.Status is ToolCallStatus.AwaitingAnswer or ToolCallStatus.AwaitingApproval || call.PendingHosts.Count > 0);

    /// <summary>
    /// Answer a question that waits for the user.
    /// </summary>
    /// <remarks>
    /// Picked options must be offered by the question, one only unless it allows several; text is cut at <see cref="UserAnswer.MaxTextLength"/>.
    /// With two tabs open the first answer wins and the other gets false.
    /// </remarks>
    /// <param name="callId">The id of the <c>ask_user</c> call, from <see cref="ToolCallEntry.CallId"/>.</param>
    /// <param name="answer">How the user answered.</param>
    /// <returns>True when the question was waiting and the answer was taken; false when it had been answered or stopped, or the answer does not fit the question.</returns>
    public bool Answer(string callId, UserAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(callId);
        ArgumentNullException.ThrowIfNull(answer);

        if (!_questions.TryGetValue(callId, out var pending))
        {
            return false;
        }

        var text = answer.Text.Length > UserAnswer.MaxTextLength ? answer.Text[..UserAnswer.MaxTextLength] : answer.Text;
        var fits = answer.Kind switch
        {
            AnswerKind.Chose => FitsOptions(pending.Question, answer.Selected),
            AnswerKind.Answered => text.Length > 0,
            _ => true,
        };

        return fits && pending.Answer.TrySetResult(answer with { Text = text });
    }

    /// <inheritdoc />
    Task<UserAnswer> IUserAsker.AskAsync(string callId, UserQuestion question, CancellationToken cancellationToken)
    {
        return WaitForAnswerAsync(callId, question, cancellationToken);
    }

    private static bool FitsOptions(UserQuestion question, IReadOnlyList<string> selected)
    {
        return selected.Count > 0
            && (question.MultiSelect || selected.Count == 1)
            && selected.Distinct(StringComparer.Ordinal).Count() == selected.Count
            && selected.All(label => question.Options.Any(option => option.Label == label));
    }

    /// <summary>
    /// Register a question and wait for its answer.
    /// </summary>
    /// <remarks>
    /// Register before the first await because the loop announces the question only after this call returns.
    /// Registration also precedes the settings check, and SetPermissionsAsync updates settings before sweeping,
    /// so a switch to Never ask cannot miss the question.
    /// </remarks>
    /// <param name="callId">The tool call asking the question.</param>
    /// <param name="question">The question to register.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    private async Task<UserAnswer> WaitForAnswerAsync(string callId, UserQuestion question, CancellationToken cancellationToken)
    {
        var waiting = new TaskCompletionSource<UserAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        _questions[callId] = new PendingQuestion(question, waiting);
        using var registration = cancellationToken.Register(() => waiting.TrySetCanceled(cancellationToken));
        LeaveQuestionsToModelIfNeverAsk();
        try
        {
            return await waiting.Task;
        }
        finally
        {
            _questions.TryRemove(callId, out _);
        }
    }

    /// <summary>
    /// Answer pending questions with "You decide" when approvals are set to Never ask.
    /// </summary>
    /// <remarks>A chat switched to Never ask has nobody to wait for.</remarks>
    private void LeaveQuestionsToModelIfNeverAsk()
    {
        if (Permissions.Settings.Approvals != ApprovalMode.NeverAsk)
        {
            return;
        }

        foreach (var pending in _questions.Values)
        {
            pending.Answer.TrySetResult(UserAnswer.LeaveToModel());
        }
    }

    /// <summary>
    /// Try to use the user's text as the answer to a waiting question.
    /// </summary>
    /// <remarks>A message written while a question waits is its answer, not a message for the reply to pick up later.</remarks>
    /// <param name="text">The user's message.</param>
    private bool TryAnswerWithText(string text)
    {
        return _questions.Keys.Any(callId => Answer(callId, UserAnswer.Answered(text)));
    }

    private sealed record PendingQuestion(UserQuestion Question, TaskCompletionSource<UserAnswer> Answer);
}
