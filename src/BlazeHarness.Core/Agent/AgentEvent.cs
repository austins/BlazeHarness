namespace BlazeHarness.Core.Agent;

/// <summary>
/// Base type of everything an agent run emits.
/// </summary>
/// <remarks>
/// The UI (or a future CLI) only consumes this stream; it never calls into the agent loop's internals.
/// </remarks>
public abstract record AgentEvent;

/// <summary>
/// A chunk of the assistant's visible reply.
/// </summary>
/// <param name="Text">The text to append to the reply being streamed.</param>
public sealed record TextDelta(string Text) : AgentEvent;

/// <summary>
/// A chunk of the model's reasoning ("thinking") text.
/// </summary>
/// <param name="Text">The text to append to the reasoning being streamed.</param>
public sealed record ReasoningDelta(string Text) : AgentEvent;

/// <summary>
/// A new assistant message begins because the model is being called again with the results of its tool calls.
/// </summary>
public sealed record NewResponse : AgentEvent;

/// <summary>
/// The model requested a tool call. Its response is complete, and the call has not started.
/// </summary>
/// <param name="CallId">The provider's identifier for the call, used to pair it with its result.</param>
/// <param name="Name">The name of the tool being called.</param>
/// <param name="ArgumentsJson">The call arguments as a JSON object string.</param>
/// <param name="Summary">A few words describing the call, or an empty string.</param>
public sealed record ToolCallRequested(string CallId, string Name, string ArgumentsJson, string Summary) : AgentEvent;

/// <summary>
/// A tool call waits for the user to allow or deny it.
/// </summary>
/// <param name="CallId">The identifier of the call, matching <see cref="ToolCallRequested.CallId"/>.</param>
/// <param name="Reason">What the user is asked to allow.</param>
/// <param name="CanRemember">True when the user may allow the same kind of call for the rest of the session.</param>
public sealed record ToolAwaitingApproval(string CallId, string Reason, bool CanRemember) : AgentEvent;

/// <summary>
/// A tool call started running.
/// </summary>
/// <param name="CallId">The identifier of the call, matching <see cref="ToolCallRequested.CallId"/>.</param>
public sealed record ToolCallRunning(string CallId) : AgentEvent;

/// <summary>
/// A tool call ended, whether it succeeded, failed, was denied or was cancelled.
/// </summary>
/// <param name="CallId">The identifier of the call, matching <see cref="ToolCallRequested.CallId"/>.</param>
/// <param name="Name">The name of the tool that was called.</param>
/// <param name="Output">The text of the result, or the reason the call did not run.</param>
/// <param name="Status">How the call ended.</param>
public sealed record ToolCallFinished(string CallId, string Name, string Output, ToolCallStatus Status) : AgentEvent;

/// <summary>
/// Token counts as reported by the endpoint.
/// </summary>
/// <param name="PromptTokens">The prompt size, which is also the current context size.</param>
/// <param name="CompletionTokens">The number of tokens the model generated.</param>
/// <param name="CachedTokens">The part of the prompt served from the provider's prompt cache, or 0 when not reported.</param>
public sealed record UsageReported(long PromptTokens, long CompletionTokens, long CachedTokens) : AgentEvent;

/// <summary>
/// Older messages were replaced by a summary, before the next model call.
/// </summary>
/// <param name="Summary">The summary that stands in for the messages before <paramref name="FirstKept"/>.</param>
/// <param name="FirstKept">The index, in the conversation without its system prompt, of the first message that is still sent as it was.</param>
/// <param name="TokensBefore">The estimated size of the conversation before compacting.</param>
/// <param name="TokensAfter">The estimated size of the conversation after compacting.</param>
public sealed record ContextCompacted(string Summary, int FirstKept, long TokensBefore, long TokensAfter) : AgentEvent;

/// <summary>
/// The conversation could not be compacted, so the run carries on with the full history.
/// </summary>
/// <param name="Message">Why compacting failed.</param>
public sealed record ContextCompactionFailed(string Message) : AgentEvent;

/// <summary>
/// The run hit an error it could not recover from.
/// </summary>
/// <param name="Message">A user-readable description of the failure.</param>
public sealed record AgentError(string Message) : AgentEvent;

/// <summary>
/// Why an agent run ended.
/// </summary>
public enum StopReason
{
    /// <summary>The model finished without requesting more tool calls.</summary>
    Completed,

    /// <summary>The user stopped the run.</summary>
    Cancelled,

    /// <summary>The run failed; an <see cref="AgentError"/> precedes this event.</summary>
    Error,

    /// <summary>The run reached its limit on model calls; an <see cref="AgentError"/> precedes this event.</summary>
    MaxIterations,
}

/// <summary>
/// The final event of every run.
/// </summary>
/// <param name="Reason">Why the run ended.</param>
public sealed record Done(StopReason Reason) : AgentEvent;
