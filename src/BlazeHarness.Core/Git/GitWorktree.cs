namespace BlazeHarness.Core.Git;

/// <summary>
/// A worktree that was just created.
/// </summary>
/// <param name="FolderPath">The folder inside the worktree that matches the folder it was created from.</param>
/// <param name="GitDirectory">The repository's shared git folder, which git in the worktree writes to.</param>
public sealed record GitWorktree(string FolderPath, string GitDirectory);
