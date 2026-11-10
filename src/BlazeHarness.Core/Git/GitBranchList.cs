namespace BlazeHarness.Core.Git;

/// <summary>
/// The branches of a repository as one working tree sees them.
/// </summary>
/// <param name="Current">The branch checked out in this working tree, or null when HEAD is detached.</param>
/// <param name="DetachedAt">The short commit id HEAD points at when it is detached, or null.</param>
/// <param name="Branches">The local branches, sorted by name.</param>
/// <param name="HasChanges">Whether tracked files have uncommitted changes, which block switching.</param>
public sealed record GitBranchList(string? Current, string? DetachedAt, IReadOnlyList<GitBranch> Branches, bool HasChanges);
