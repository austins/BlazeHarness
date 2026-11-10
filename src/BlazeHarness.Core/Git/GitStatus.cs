namespace BlazeHarness.Core.Git;

/// <summary>
/// The changed files of a repository.
/// </summary>
/// <param name="Root">The full path of the repository root.</param>
/// <param name="Files">The changed files in the scope that was asked for, ordered by path.</param>
/// <param name="AllCount">How many files differ from the last commit.</param>
/// <param name="StagedCount">How many files have staged changes.</param>
/// <param name="UnstagedCount">How many files have changes that are not staged, or are not tracked.</param>
public sealed record GitStatus(string Root, IReadOnlyList<GitChangedFile> Files, int AllCount, int StagedCount, int UnstagedCount)
{
    /// <summary>
    /// Check whether another status holds exactly the same information.
    /// </summary>
    /// <param name="other">The status to compare with, or null.</param>
    /// <returns>True when the root, the three counts and every file, in the same order, are equal.</returns>
    public bool IsSameAs(GitStatus? other) =>
        other is not null
        && Root == other.Root
        && AllCount == other.AllCount
        && StagedCount == other.StagedCount
        && UnstagedCount == other.UnstagedCount
        && Files.SequenceEqual(other.Files);
}
