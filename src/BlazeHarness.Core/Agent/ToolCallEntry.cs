using BlazeHarness.Core.Agent.Questions;
using BlazeHarness.Core.Attachments;
using BlazeHarness.Core.Diff;
using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Agent;

/// <summary>
/// One tool call the model made in a reply, with its result once it has one.
/// </summary>
/// <remarks>
/// A call is updated from the thread that runs it, and each property is replaced as a whole value.
/// </remarks>
public sealed class ToolCallEntry
{
    /// <summary>
    /// The text that starts the result of a failed call in what the model is given, as written by <see cref="ForModel"/>.
    /// </summary>
    public const string ErrorPrefix = "Error: ";

    private OutputTail? _live;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolCallEntry"/> class.
    /// </summary>
    /// <param name="callId">The provider's identifier for the call.</param>
    /// <param name="name">The name of the tool.</param>
    /// <param name="argumentsJson">The call arguments as a JSON object string.</param>
    /// <param name="summary">A few words describing the call, or an empty string.</param>
    public ToolCallEntry(string callId, string name, string argumentsJson, string summary)
    {
        CallId = callId;
        Name = name;
        ArgumentsJson = argumentsJson;
        Summary = summary;
    }

    /// <summary>
    /// Gets the provider's identifier for the call, which pairs it with its result.
    /// </summary>
    public string CallId { get; }

    /// <summary>
    /// Gets the name of the tool.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the call arguments as a JSON object string.
    /// </summary>
    public string ArgumentsJson { get; }

    /// <summary>
    /// Gets a few words describing the call, or an empty string.
    /// </summary>
    public string Summary { get; }

    /// <summary>
    /// Gets or sets where the call is in its life.
    /// </summary>
    public ToolCallStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the text the call returned, or the reason it did not run.
    /// </summary>
    public string Output { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the copies, in the chat's attachments, of the pictures the call produced.
    /// </summary>
    public IReadOnlyList<AttachmentInfo> Images { get; set; } = [];

    /// <summary>
    /// Gets or sets what the user is asked to allow while <see cref="Status"/> is <see cref="ToolCallStatus.AwaitingApproval"/>. It is not saved.
    /// </summary>
    public string ApprovalReason { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the user may allow the same kind of call for the rest of the chat. It is not saved.
    /// </summary>
    public bool CanRemember { get; set; }

    /// <summary>
    /// Gets or sets the hosts a running command waits to connect to until the user answers. It is not saved.
    /// </summary>
    public IReadOnlyList<PendingHost> PendingHosts { get; set; } = [];

    /// <summary>
    /// Gets or sets when the call started running, or null when it has not run in this session. It is not saved.
    /// </summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// Gets the last lines a running command printed, separated by <c>\n</c>, or an empty string. It is not saved.
    /// </summary>
    /// <remarks>
    /// The whole output is in <see cref="Output"/> once the call ends, and this is cleared then.
    /// </remarks>
    public string LiveOutput => Volatile.Read(ref _live)?.Text ?? string.Empty;

    /// <summary>
    /// Gets or sets how a file call would change the file, worked out before the call ran, or null when there is none. It is not saved.
    /// </summary>
    public IReadOnlyList<DiffLine>? ChangePreview { get; set; }

    /// <summary>
    /// Gets the question of an <c>ask_user</c> call, read from its arguments, or null for any other call or when the arguments are not a valid question.
    /// </summary>
    public UserQuestion? Question => Name == UserQuestion.ToolName ? AskUserTool.TryParse(ArgumentsJson) : null;

    /// <summary>
    /// Gets a value indicating whether the call has ended, one way or another.
    /// </summary>
    public bool IsFinished => Status is ToolCallStatus.Succeeded or ToolCallStatus.Failed or ToolCallStatus.Denied or ToolCallStatus.Cancelled or ToolCallStatus.Interrupted;

    /// <summary>
    /// Get the text the model is given as the result of a call.
    /// </summary>
    /// <param name="status">How the call ended.</param>
    /// <param name="output">The output of the call.</param>
    /// <returns>The output, marked as an error when the call failed so the model does not mistake it for data.</returns>
    public static string ForModel(ToolCallStatus status, string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return status == ToolCallStatus.Failed ? ErrorPrefix + output : output;
    }

    /// <summary>
    /// Get the text the model is given as the result of a call that never finished.
    /// </summary>
    /// <param name="status">Either <see cref="ToolCallStatus.Cancelled"/> or <see cref="ToolCallStatus.Interrupted"/>.</param>
    /// <param name="toolName">The name of the tool, because a question that was not answered is not one that "may or may not have run".</param>
    /// <returns>A sentence saying the call did not complete.</returns>
    public static string Unfinished(ToolCallStatus status, string toolName)
    {
        if (toolName == UserQuestion.ToolName)
        {
            return status == ToolCallStatus.Interrupted ? AnswerText.Interrupted : AnswerText.Stopped;
        }

        return status == ToolCallStatus.Interrupted
            ? "The app stopped before this call finished, so it may or may not have run. Check the current state before doing it again."
            : "The user stopped the run before this call finished, so it may or may not have run. Check the current state before doing it again.";
    }

    /// <summary>
    /// Add a line a running command printed to <see cref="LiveOutput"/>.
    /// </summary>
    /// <param name="line">The line.</param>
    internal void AppendLiveLine(string line) => LazyInitializer.EnsureInitialized(ref _live).Append(line);

    /// <summary>
    /// Clear <see cref="LiveOutput"/> once the call has ended.
    /// </summary>
    internal void EndLiveOutput() => Volatile.Write(ref _live, null);
}
