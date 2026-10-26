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
/// The model requested a tool call and it is about to run.
/// </summary>
/// <param name="CallId">The provider's identifier for the call, used to pair it with its result.</param>
/// <param name="Name">The name of the tool being called.</param>
/// <param name="ArgumentsJson">The call arguments as a JSON object string.</param>
public sealed record ToolCallStarted(string CallId, string Name, string ArgumentsJson) : AgentEvent;

/// <summary>
/// A tool call completed, successfully or not.
/// </summary>
/// <param name="CallId">The identifier of the call that finished, matching <see cref="ToolCallStarted.CallId"/>.</param>
/// <param name="Name">The name of the tool that was called.</param>
/// <param name="Output">The text sent back to the model as the tool result.</param>
/// <param name="IsError">Whether the call failed, was denied, or was blocked.</param>
public sealed record ToolCallFinished(string CallId, string Name, string Output, bool IsError) : AgentEvent;

/// <summary>
/// Token counts as reported by the endpoint.
/// </summary>
/// <param name="PromptTokens">The prompt size, which is also the current context size.</param>
/// <param name="CompletionTokens">The number of tokens the model generated.</param>
/// <param name="CachedTokens">The part of the prompt served from the provider's prompt cache, or 0 when not reported.</param>
public sealed record UsageReported(long PromptTokens, long CompletionTokens, long CachedTokens) : AgentEvent;

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

    /// <summary>The run reached its limit on model round trips.</summary>
    MaxIterations,
}

/// <summary>
/// The final event of every run.
/// </summary>
/// <param name="Reason">Why the run ended.</param>
public sealed record Done(StopReason Reason) : AgentEvent;
