namespace BlazeHarness.Core.Compaction;

/// <summary>
/// What a compaction marker in the conversation stands for.
/// </summary>
/// <param name="Summary">The summary that replaces every message above the marker when the conversation is sent to the model.</param>
/// <param name="TokensBefore">The estimated size of the conversation before compacting.</param>
/// <param name="TokensAfter">The estimated size of the conversation after compacting.</param>
public sealed record CompactionInfo(string Summary, long TokensBefore, long TokensAfter);
