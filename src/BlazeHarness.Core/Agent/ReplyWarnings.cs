namespace BlazeHarness.Core.Agent;

/// <summary>
/// Problems with a reply that the chat shows next to it.
/// </summary>
[Flags]
public enum ReplyWarnings
{
    /// <summary>No problem.</summary>
    None = 0,

    /// <summary>The reply was cut off at the output token limit.</summary>
    ResponseTruncated = 1,

    /// <summary>Compacting the conversation failed, so the rest of the reply runs on the full context.</summary>
    CompactionFailed = 2,

    /// <summary>The reply was stopped because the conversation no longer fits the model's context and could not be compacted.</summary>
    ContextTooLarge = 4,
}
