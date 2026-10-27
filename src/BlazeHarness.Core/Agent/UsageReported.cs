namespace BlazeHarness.Core.Agent;

/// <summary>
/// Token counts as reported by the endpoint.
/// </summary>
/// <param name="PromptTokens">The prompt size, which is also the current context size.</param>
/// <param name="CompletionTokens">The number of tokens the model generated.</param>
/// <param name="CachedTokens">The part of the prompt served from the provider's prompt cache, or 0 when not reported.</param>
public sealed record UsageReported(long PromptTokens, long CompletionTokens, long CachedTokens) : AgentEvent;
