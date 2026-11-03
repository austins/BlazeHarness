namespace BlazeHarness.Core.Agent.Questions;

/// <summary>
/// A question the model asks the user with the <c>ask_user</c> tool.
/// </summary>
/// <param name="Question">The question, as plain text.</param>
/// <param name="Options">The answers the model offers, or none for an open question. The user may always answer in their own words.</param>
/// <param name="MultiSelect">True when the user may pick more than one option.</param>
public sealed record UserQuestion(string Question, IReadOnlyList<QuestionOption> Options, bool MultiSelect = false)
{
    /// <summary>
    /// The name the model calls the tool that asks a question by.
    /// </summary>
    public const string ToolName = "ask_user";

    /// <summary>
    /// The most characters a question may have.
    /// </summary>
    public const int MaxQuestionLength = 2_000;

    /// <summary>
    /// The fewest options a question with options has.
    /// </summary>
    public const int MinOptions = 2;

    /// <summary>
    /// The most options a question may have.
    /// </summary>
    public const int MaxOptions = 8;

    /// <summary>
    /// The most characters an option's label may have.
    /// </summary>
    public const int MaxLabelLength = 100;

    /// <summary>
    /// The most characters an option's description may have.
    /// </summary>
    public const int MaxDescriptionLength = 300;
}
