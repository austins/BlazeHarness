namespace BlazeHarness.Core.Agent.Questions;

/// <summary>
/// One answer the model offers for a question.
/// </summary>
/// <param name="Label">A short answer, which is what the model gets back when the user picks it.</param>
/// <param name="Description">What the choice means, or null when the label says it all.</param>
public sealed record QuestionOption(string Label, string? Description = null);
