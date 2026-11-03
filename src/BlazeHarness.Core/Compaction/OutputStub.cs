using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Agent.Questions;
using BlazeHarness.Core.Skills;

namespace BlazeHarness.Core.Compaction;

/// <summary>
/// The text that stands in for a large tool result in the request, once the result is old enough that compaction trims it.
/// </summary>
/// <remarks>
/// The stub is the same for every result, so the request stays the same from one call to the next and the server's prompt cache keeps working.
/// The full output stays in the chat and its log; only the request is shorter.
/// </remarks>
internal static class OutputStub
{
    /// <summary>
    /// The size in estimated tokens from which a result is replaced; smaller results cost about as much as the stub.
    /// </summary>
    public const int MinTokens = 300;

    /// <summary>
    /// The text that replaces the output.
    /// </summary>
    public const string Text =
        "[The output of this call was removed to save context. To see it again, repeat the call (read the file or run the command again), "
        + "or find a part of it with search_chat.]";

    private const int MaxErrorLineCharacters = 300;

    /// <summary>
    /// Get the stub for a tool result, if it is one that may be replaced.
    /// </summary>
    /// <remarks>
    /// The results of <c>update_todos</c>, <c>load_skill</c> and <c>ask_user</c> are never replaced: the to-do list is read from them,
    /// a skill's instructions are not something to repeat a call for, and an answer is the user's own words.
    /// A failed call keeps the first line of its error, so the model still sees what went wrong.
    /// </remarks>
    /// <param name="toolName">The name of the tool that made the call.</param>
    /// <param name="result">The text the model was given as the result.</param>
    /// <returns>The text to send instead, or null when the result is sent as it is.</returns>
    public static string? For(string toolName, string? result)
    {
        ArgumentNullException.ThrowIfNull(toolName);

        if (toolName is TodoList.ToolName or LoadSkillTool.ToolName or UserQuestion.ToolName
            || result is null
            || TokenEstimate.Of(result) < MinTokens
            || result == Text)
        {
            return null;
        }

        if (!result.StartsWith(ToolCallEntry.ErrorPrefix, StringComparison.Ordinal))
        {
            return Text;
        }

        var line = result.AsSpan();
        var end = line.IndexOfAny('\r', '\n');
        line = end < 0 ? line : line[..end];
        var cut = line.Length > MaxErrorLineCharacters && char.IsHighSurrogate(line[MaxErrorLineCharacters - 1]) ? MaxErrorLineCharacters - 1 : MaxErrorLineCharacters;
        return (line.Length > MaxErrorLineCharacters ? string.Concat(line[..cut], "…") : line.ToString()) + "\n" + Text;
    }
}
