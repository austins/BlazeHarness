namespace BlazeHarness.Core.Agent.Questions;

/// <summary>
/// How the user answered a question.
/// </summary>
public enum AnswerKind
{
    /// <summary>The user picked one or more of the offered options, and may have added text.</summary>
    Chose,

    /// <summary>The user answered in their own words.</summary>
    Answered,

    /// <summary>The user chose not to answer.</summary>
    Skipped,

    /// <summary>The user left the decision to the model.</summary>
    YouDecide,
}
