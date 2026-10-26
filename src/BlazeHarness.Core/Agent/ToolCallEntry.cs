namespace BlazeHarness.Core.Agent;

/// <summary>
/// Where a tool call is in its life, as the UI shows it.
/// </summary>
public enum ToolCallStatus
{
    /// <summary>The model asked for the call and it has not started.</summary>
    Pending,

    /// <summary>The call is waiting for the user to allow or deny it.</summary>
    AwaitingApproval,

    /// <summary>The call is running.</summary>
    Running,

    /// <summary>The call ran and returned a result.</summary>
    Succeeded,

    /// <summary>The call could not run or returned an error.</summary>
    Failed,

    /// <summary>The user denied the call, so it did not run.</summary>
    Denied,

    /// <summary>The user stopped the run before the call finished.</summary>
    Cancelled,

    /// <summary>The app stopped before the call finished.</summary>
    Interrupted,
}

/// <summary>
/// One tool call the model made in a reply, with its result once it has one.
/// </summary>
/// <remarks>
/// A call is updated from the thread that runs it, and each property is replaced as a whole value.
/// </remarks>
public sealed class ToolCallEntry
{
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
        return status == ToolCallStatus.Failed ? $"Error: {output}" : output;
    }

    /// <summary>
    /// Get the text the model is given as the result of a call that never finished.
    /// </summary>
    /// <param name="status">Either <see cref="ToolCallStatus.Cancelled"/> or <see cref="ToolCallStatus.Interrupted"/>.</param>
    /// <returns>A sentence saying the call did not complete.</returns>
    public static string Unfinished(ToolCallStatus status)
    {
        return status == ToolCallStatus.Interrupted
            ? "The app stopped before this call finished, so it may or may not have run."
            : "The user stopped the run before this call finished, so it may or may not have run.";
    }
}
