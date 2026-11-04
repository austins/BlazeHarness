namespace BlazeHarness.Core.Git;

/// <summary>
/// Which changes of a repository to look at.
/// </summary>
public enum GitChangeScope
{
    /// <summary>
    /// Everything that differs from the last commit: staged and unstaged changes and new files.
    /// </summary>
    All,

    /// <summary>
    /// Only what is staged for the next commit.
    /// </summary>
    Staged,

    /// <summary>
    /// Only what is not staged yet, including new files that git does not track.
    /// </summary>
    Unstaged,
}
