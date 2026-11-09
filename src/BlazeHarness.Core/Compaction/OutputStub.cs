using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Agent.Questions;
using BlazeHarness.Core.Skills;
using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Compaction;

/// <summary>
/// The text that stands in for a large tool result in the request, once the result is old enough that compaction trims it.
/// </summary>
/// <remarks>
/// The stub of a result depends only on its call, so the request stays the same from one call to the next and the server's prompt cache keeps working.
/// The full output stays in the chat and its log; only the request is shorter. The stub names the call's ID, because many chat templates
/// don't show the IDs to the model, and <c>search_chat</c> reads the output again by that ID.
/// </remarks>
internal static class OutputStub
{
    /// <summary>
    /// The size in estimated tokens from which a result is replaced; smaller results cost about as much as the stub.
    /// </summary>
    public const int MinTokens = 300;

    /// <summary>
    /// The start of every stub.
    /// </summary>
    public const string Marker = "[The output of this call was removed to save context.";

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
    /// <param name="callId">The ID of the call.</param>
    /// <param name="result">The text the model was given as the result.</param>
    /// <returns>The text to send instead, or null when the result is sent as it is.</returns>
    public static string? For(string toolName, string callId, string? result)
    {
        ArgumentNullException.ThrowIfNull(toolName);
        ArgumentNullException.ThrowIfNull(callId);

        if (toolName is TodoList.ToolName or LoadSkillTool.ToolName or UserQuestion.ToolName
            || result is null
            || TokenEstimate.Of(result) < MinTokens
            || IsStub(result))
        {
            return null;
        }

        if (!result.StartsWith(ToolCallEntry.ErrorPrefix, StringComparison.Ordinal))
        {
            return Text(callId);
        }

        var line = result.AsSpan();
        var end = line.IndexOfAny('\r', '\n');
        line = end < 0 ? line : line[..end];
        var cut = line.Length > MaxErrorLineCharacters && char.IsHighSurrogate(line[MaxErrorLineCharacters - 1]) ? MaxErrorLineCharacters - 1 : MaxErrorLineCharacters;
        return (line.Length > MaxErrorLineCharacters ? string.Concat(line[..cut], "…") : line.ToString()) + "\n" + Text(callId);
    }

    /// <summary>
    /// Get the stub that replaces the output of a call.
    /// </summary>
    /// <param name="callId">The ID of the call.</param>
    /// <returns>The stub, which names the call so its output can be read again.</returns>
    public static string Text(string callId) =>
        $"{Marker} To read it again, call {SearchChatTool.ToolName} with ref \"{callId}\", or repeat the call (read the file or run the command again).]";

    /// <summary>
    /// Get whether a result is a stub, after an error line or not.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <returns>True when the result is or ends with a stub.</returns>
    public static bool IsStub(string? result) =>
        result is not null && (result.StartsWith(Marker, StringComparison.Ordinal) || result.Contains("\n" + Marker, StringComparison.Ordinal));
}
