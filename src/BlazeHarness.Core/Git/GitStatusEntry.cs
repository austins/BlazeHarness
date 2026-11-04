namespace BlazeHarness.Core.Git;

/// <summary>
/// One line of <c>git status --porcelain</c>.
/// </summary>
/// <param name="Path">The path relative to the repository root.</param>
/// <param name="Index">The status letter of the index (the staged side), or a space.</param>
/// <param name="WorkTree">The status letter of the working tree (the unstaged side), or a space.</param>
internal sealed record GitStatusEntry(string Path, char Index, char WorkTree)
{
    /// <summary>
    /// Gets a value indicating whether git does not track the file.
    /// </summary>
    public bool IsUntracked => Index == '?';

    /// <summary>
    /// Gets a value indicating whether the file has an unresolved merge conflict.
    /// </summary>
    public bool IsConflicted => Index == 'U' || WorkTree == 'U' || (Index == 'A' && WorkTree == 'A') || (Index == 'D' && WorkTree == 'D');

    /// <summary>
    /// Gets a value indicating whether the file has staged changes.
    /// </summary>
    public bool IsStaged => !IsUntracked && !IsConflicted && Index != ' ';

    /// <summary>
    /// Gets a value indicating whether the file has unstaged changes or is untracked.
    /// </summary>
    public bool IsUnstaged => IsUntracked || IsConflicted || WorkTree != ' ';
}
