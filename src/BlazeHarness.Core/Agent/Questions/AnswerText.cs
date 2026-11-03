namespace BlazeHarness.Core.Agent.Questions;

/// <summary>
/// The fixed wording of the results an <c>ask_user</c> call can end with, which the model reads and the UI reads back.
/// </summary>
/// <remarks>
/// Each kind of answer has its own prefix and is saved as a successful call. A result that starts with <see cref="ToolCallEntry.ErrorPrefix"/>
/// would make the extractive summary record a failed call.
/// </remarks>
public static class AnswerText
{
    /// <summary>
    /// Starts the result when the user picked options; the quoted labels follow.
    /// </summary>
    public const string ChosePrefix = "The user chose: ";

    /// <summary>
    /// Follows the picked options when the user also typed text; the text follows.
    /// </summary>
    public const string AddedPrefix = " They added: ";

    /// <summary>
    /// Starts the result when the user answered in their own words; the text follows.
    /// </summary>
    public const string AnsweredPrefix = "The user answered: ";

    /// <summary>
    /// The result when the user skipped the question.
    /// </summary>
    public const string Skipped = "The user skipped this question. Don't ask it again; continue with your best judgment and say what you assumed.";

    /// <summary>
    /// The result when the user left the decision to the model.
    /// </summary>
    public const string YouDecide = "The user left this to you. Choose what you think is best and say what you chose.";

    /// <summary>
    /// The result when the run was stopped while the question waited.
    /// </summary>
    public const string Stopped = "The run was stopped before the user answered.";

    /// <summary>
    /// The result when the app stopped while the question waited.
    /// </summary>
    public const string Interrupted = "The app stopped before the user answered. Treat the user's next message as the reply; ask again only if it still matters.";
}
