namespace BlazeHarness.Core.Agent.Questions;

/// <summary>
/// How the user answered a question, and the text the model gets for it.
/// </summary>
/// <param name="Kind">How the user answered.</param>
/// <param name="Selected">The labels of the options the user picked; empty unless <paramref name="Kind"/> is <see cref="AnswerKind.Chose"/>.</param>
/// <param name="Text">The text the user wrote, or an empty string when none.</param>
public sealed record UserAnswer(AnswerKind Kind, IReadOnlyList<string> Selected, string Text)
{
    /// <summary>
    /// The most characters the user's own words may have.
    /// </summary>
    public const int MaxTextLength = 10_000;

    private const string QuoteSeparator = "\", \"";

    /// <summary>
    /// Gets a short description of the answer for the chat, such as the picked labels or the text.
    /// </summary>
    public string Display => Kind switch
    {
        AnswerKind.Chose => Text.Length > 0 ? $"{string.Join(", ", Selected)} — {Text}" : string.Join(", ", Selected),
        AnswerKind.Answered => Text,
        AnswerKind.Skipped => "Skipped",
        _ => "You decide",
    };

    /// <summary>
    /// Create the answer of a user who picked options.
    /// </summary>
    /// <param name="selected">The labels of the picked options.</param>
    /// <param name="text">What the user added, or null.</param>
    /// <returns>The answer.</returns>
    public static UserAnswer Chose(IEnumerable<string> selected, string? text = null)
    {
        ArgumentNullException.ThrowIfNull(selected);
        return new UserAnswer(AnswerKind.Chose, [.. selected], text?.Trim() ?? string.Empty);
    }

    /// <summary>
    /// Create the answer of a user who wrote their own.
    /// </summary>
    /// <param name="text">What the user wrote.</param>
    /// <returns>The answer.</returns>
    public static UserAnswer Answered(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new UserAnswer(AnswerKind.Answered, [], text.Trim());
    }

    /// <summary>
    /// Create the answer of a user who skipped the question.
    /// </summary>
    /// <returns>The answer.</returns>
    public static UserAnswer Skip() => new(AnswerKind.Skipped, [], string.Empty);

    /// <summary>
    /// Create the answer of a user who left the decision to the model.
    /// </summary>
    /// <returns>The answer.</returns>
    public static UserAnswer LeaveToModel() => new(AnswerKind.YouDecide, [], string.Empty);

    /// <summary>
    /// Read an answer back from the result of an <c>ask_user</c> call.
    /// </summary>
    /// <param name="output">The result text the call saved.</param>
    /// <returns>The answer, or null when the text is not one of the answer results, such as a stop notice.</returns>
    public static UserAnswer? TryRead(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (output == AnswerText.Skipped)
        {
            return Skip();
        }

        if (output == AnswerText.YouDecide)
        {
            return LeaveToModel();
        }

        if (output.StartsWith(AnswerText.AnsweredPrefix, StringComparison.Ordinal))
        {
            return Answered(output[AnswerText.AnsweredPrefix.Length..]);
        }

        if (!output.StartsWith(AnswerText.ChosePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var body = output[AnswerText.ChosePrefix.Length..];
        var added = string.Empty;
        var split = body.IndexOf("\"." + AnswerText.AddedPrefix, StringComparison.Ordinal);
        if (split >= 0)
        {
            added = body[(split + 2 + AnswerText.AddedPrefix.Length)..];
            body = body[..(split + 2)];
        }

        if (body.Length < 3 || body[0] != '"' || !body.EndsWith("\".", StringComparison.Ordinal))
        {
            return null;
        }

        return Chose(body[1..^2].Split(QuoteSeparator), added);
    }

    /// <summary>
    /// Get the text the model is given as the result of the call.
    /// </summary>
    /// <returns>The answer in the wording of <see cref="AnswerText"/>.</returns>
    public string ForModel()
    {
        return Kind switch
        {
            AnswerKind.Chose => $"{AnswerText.ChosePrefix}{string.Join(", ", Selected.Select(label => $"\"{label}\""))}." + (Text.Length > 0 ? AnswerText.AddedPrefix + Text : string.Empty),
            AnswerKind.Answered => AnswerText.AnsweredPrefix + Text,
            AnswerKind.Skipped => AnswerText.Skipped,
            _ => AnswerText.YouDecide,
        };
    }
}
