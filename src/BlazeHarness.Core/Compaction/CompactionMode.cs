namespace BlazeHarness.Core.Compaction;

/// <summary>
/// How the summary that replaces older messages is written.
/// </summary>
public enum CompactionMode
{
    /// <summary>
    /// The model writes the summary, with a request of its own.
    /// </summary>
    Model,

    /// <summary>
    /// The summary is built from the messages by code, with no model call, so the same messages always give the same summary.
    /// </summary>
    Extractive,
}
