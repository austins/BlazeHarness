namespace BlazeHarness.Core.Git;

/// <summary>
/// One changed file of a repository.
/// </summary>
/// <param name="Path">The path relative to the repository root, with forward slashes.</param>
/// <param name="Kind">How the file changed in the scope that was asked for.</param>
/// <param name="Staged">Whether the file has changes staged for the next commit.</param>
/// <param name="Unstaged">Whether the file has changes that are not staged, or is not tracked.</param>
/// <param name="Added">The number of lines added, or null when it is not known or the file is binary.</param>
/// <param name="Removed">The number of lines removed, or null when it is not known or the file is binary.</param>
/// <param name="IsBinary">Whether git treats the file as binary.</param>
/// <param name="Stamp">Identifies the file's current state, so a caller can tell whether it changed since it looked. It differs whenever the working-tree file's last-write time or length, its kind, the staged or unstaged flags or the line counts differ.</param>
public sealed record GitChangedFile(
    string Path,
    GitChangeKind Kind,
    bool Staged,
    bool Unstaged,
    int? Added,
    int? Removed,
    bool IsBinary,
    string Stamp);
