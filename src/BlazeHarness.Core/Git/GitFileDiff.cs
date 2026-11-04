namespace BlazeHarness.Core.Git;

/// <summary>
/// The changes to one file.
/// </summary>
/// <param name="Path">The path relative to the repository root.</param>
/// <param name="IsBinary">Whether the file is binary, so there are no lines to show.</param>
/// <param name="IsTruncated">Whether the diff was cut off because it is too large.</param>
/// <param name="Hunks">The changed parts of the file.</param>
public sealed record GitFileDiff(string Path, bool IsBinary, bool IsTruncated, IReadOnlyList<GitDiffHunk> Hunks);
