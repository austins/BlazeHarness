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

    // The question must be registered before the first await, because the loop announces it only after this call returns.
    // It is registered before the settings are checked, and SetPermissionsAsync sets them before sweeping, so a switch to Never ask can't miss it.
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

    // A chat switched to Never ask has nobody to wait for, so a waiting question is answered as if the user chose "You decide".
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

    // A message written while a question waits is the answer to it, not a message for the reply to pick up later.
    private bool TryAnswerWithText(string text)
    {
        return _questions.Keys.Any(callId => Answer(callId, UserAnswer.Answered(text)));
    }

    private sealed record PendingQuestion(UserQuestion Question, TaskCompletionSource<UserAnswer> Answer);
}
