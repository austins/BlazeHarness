namespace BlazeHarness.Core.Git;

/// <summary>
/// How a file changed.
/// </summary>
public enum GitChangeKind
{
    /// <summary>
    /// The file is new.
    /// </summary>
    Added,

    /// <summary>
    /// The file's content changed.
    /// </summary>
    Modified,

    /// <summary>
    /// The file was removed.
    /// </summary>
    Deleted,

    /// <summary>
    /// The file's type changed, for example from a regular file to a symbolic link.
    /// </summary>
    TypeChanged,

    /// <summary>
    /// The file is new and git does not track it yet.
    /// </summary>
    Untracked,

    /// <summary>
    /// The file has an unresolved merge conflict.
    /// </summary>
    Conflicted,
}
