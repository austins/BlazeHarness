namespace BlazeHarness.Core.Git;

/// <summary>
/// The outcome of switching to or creating a branch, or of creating a worktree.
/// </summary>
public enum GitBranchResult
{
    /// <summary>The work was done.</summary>
    Done,

    /// <summary>The branch name breaks git's rules.</summary>
    InvalidName,

    /// <summary>A branch with the name already exists.</summary>
    Exists,

    /// <summary>The working tree has uncommitted changes, or the target is otherwise not allowed.</summary>
    NotAllowed,

    /// <summary>Git is missing, timed out or refused.</summary>
    Failed,
}
