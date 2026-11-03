namespace BlazeHarness.Core.Compaction;

/// <summary>
/// What a compaction marker in the conversation stands for.
/// </summary>
/// <param name="Summary">The summary that replaces every message above the marker when the conversation is sent to the model; empty for a trim marker.</param>
/// <param name="TokensBefore">The estimated size of the conversation before compacting.</param>
/// <param name="TokensAfter">The estimated size of the conversation after compacting.</param>
/// <param name="OutputTrimmed">True when the marker only means that large tool output above it is replaced by a stub in the request, and no summary was written.</param>
public sealed record CompactionInfo(string Summary, long TokensBefore, long TokensAfter, bool OutputTrimmed = false);
