namespace BlazeHarness.Core.Git;

/// <summary>
/// The work in a worktree that removing it would lose.
/// </summary>
/// <param name="ChangedFiles">The number of changed and untracked files.</param>
/// <param name="UnsharedCommits">The number of commits that no other branch and no remote branch has.</param>
public sealed record GitWorktreeState(int ChangedFiles, int UnsharedCommits)
{
    /// <summary>
    /// Gets a value indicating whether removing the worktree and its branch would lose nothing.
    /// </summary>
    public bool IsClean => ChangedFiles == 0 && UnsharedCommits == 0;
}
