namespace BlazeHarness.Core.Git;

/// <summary>
/// What became of a request to undo a change.
/// </summary>
public enum GitRevertResult
{
    /// <summary>
    /// The change was undone.
    /// </summary>
    Reverted,

    /// <summary>
    /// The file no longer matches what was shown, so nothing was done.
    /// </summary>
    Changed,

    /// <summary>
    /// The change is not one that may be undone this way, or its path is not safe to touch.
    /// </summary>
    NotAllowed,

    /// <summary>
    /// Git is not available, git refused, or the file could not be written.
    /// </summary>
    Failed,
}
