namespace BlazeHarness.Core.Compaction;

/// <summary>
/// How full the model's context is.
/// </summary>
/// <param name="Tokens">The size of the next request in tokens.</param>
/// <param name="Approximate">True when <paramref name="Tokens"/> is estimated from text length, not reported by the server.</param>
/// <param name="Window">The model's context window in tokens, or 0 when it is not known.</param>
/// <param name="Limit">The size at which the conversation is compacted automatically, or 0 when it never is.</param>
/// <param name="CacheHitRate">The part of the last prompt that the server's prompt cache served, from 0 to 1, or null when the server doesn't report it.</param>
public sealed record ContextStatus(long Tokens, bool Approximate, int Window, int Limit, double? CacheHitRate);
