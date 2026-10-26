namespace BlazeHarness.Core.Agent;

/// <summary>Base type of everything an agent run emits. The UI (or a future CLI) only consumes this stream.</summary>
public abstract record AgentEvent;

public sealed record TextDelta(string Text) : AgentEvent;

public sealed record ReasoningDelta(string Text) : AgentEvent;

public sealed record ToolCallStarted(string CallId, string Name, string ArgumentsJson) : AgentEvent;

public sealed record ToolCallFinished(string CallId, string Name, string Output, bool IsError) : AgentEvent;

/// <summary>Token counts as reported by the endpoint. <see cref="PromptTokens"/> is the current context size.</summary>
public sealed record UsageReported(long PromptTokens, long CompletionTokens, long CachedTokens) : AgentEvent;

public sealed record AgentError(string Message) : AgentEvent;

public enum StopReason
{
    Completed,
    Cancelled,
    Error,
    MaxIterations,
}

public sealed record Done(StopReason Reason) : AgentEvent;
