namespace BlazeHarness.Core.Git;

/// <summary>
/// A local branch of a repository.
/// </summary>
/// <param name="Name">The short branch name.</param>
/// <param name="WorktreePath">The working tree that has the branch checked out, or null when none has.</param>
public sealed record GitBranch(string Name, string? WorktreePath);
