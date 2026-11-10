namespace BlazeHarness.Core.Git;

/// <summary>
/// Creates, inspects and removes the git worktrees the app makes for chats.
/// </summary>
/// <remarks>
/// These change the user's repository, so they must only run when the user asked for it. Branch names are checked with
/// <see cref="GitBranchName.IsValid"/> first, so none can be read as an option.
/// </remarks>
public sealed class GitWorktrees
{
    private static readonly TimeSpan CheckoutTimeout = TimeSpan.FromMinutes(10);

    private readonly IGitRunner _git;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitWorktrees"/> class.
    /// </summary>
    /// <param name="time">The clock that times out a slow git program.</param>
    public GitWorktrees(TimeProvider time)
        : this(new GitProcessRunner(time))
    {
    }

    internal GitWorktrees(IGitRunner git)
    {
        _git = git;
    }

    /// <summary>
    /// Create a branch and check it out in a new worktree.
    /// </summary>
    /// <param name="folder">A folder inside the repository; the result points at the same folder inside the worktree.</param>
    /// <param name="path">Where to put the worktree; it must not exist or be empty.</param>
    /// <param name="branch">The new branch name.</param>
    /// <param name="baseBranch">The local branch to start from.</param>
    /// <param name="cancellationToken">A token that stops the work.</param>
    /// <returns>What happened, and the worktree when it was created.</returns>
    public async Task<(GitBranchResult Result, GitWorktree? Worktree)> AddAsync(string folder, string path, string branch, string baseBranch, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!GitBranchName.IsValid(branch) || !GitBranchName.IsValid(baseBranch))
        {
            return (GitBranchResult.InvalidName, null);
        }

        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
        {
            return (GitBranchResult.NotAllowed, null);
        }

        var prefix = await _git.RunAsync(folder, ["rev-parse", "--show-prefix"], cancellationToken);
        var common = await _git.RunAsync(folder, ["rev-parse", "--path-format=absolute", "--git-common-dir"], cancellationToken);
        if (!prefix.Succeeded || !common.Succeeded)
        {
            return (GitBranchResult.Failed, null);
        }

        if (await GitBranches.ExistsAsync(_git, folder, branch, cancellationToken))
        {
            return (GitBranchResult.Exists, null);
        }

        if (!await GitBranches.ExistsAsync(_git, folder, baseBranch, cancellationToken))
        {
            return (GitBranchResult.NotAllowed, null);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var done = false;
        try
        {
            var added = await _git.RunAsync(folder, ["worktree", "add", "--no-checkout", "-b", branch, path, baseBranch], cancellationToken);

            // Checking out from inside the worktree lets the runner find and turn off filters that only the new branch's
            // configuration (includeIf onbranch) sets. The worktree sits deep inside the app's folder, so long paths are
            // allowed on Windows.
            done = added.Succeeded
                && (await _git.RunAsync(path, ["-c", "core.longpaths=true", "reset", "--hard", "--quiet"], cancellationToken, CheckoutTimeout)).Succeeded;
        }
        finally
        {
            if (!done)
            {
                // The branch was checked to be new above, so deleting it removes only what this call made.
                await RemoveAsync(folder, path, branch, deleteBranch: true, CancellationToken.None);
            }
        }

        if (!done)
        {
            return (GitBranchResult.Failed, null);
        }

        var inside = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(path, prefix.Output.Trim())));
        return (GitBranchResult.Done, new GitWorktree(inside, Path.TrimEndingDirectorySeparator(Path.GetFullPath(common.Output.Trim()))));
    }

    /// <summary>
    /// Count the work in a worktree that removing it and its branch would lose.
    /// </summary>
    /// <remarks>
    /// Commits count when they are on <paramref name="branch"/> or a detached HEAD and on no other branch, so work stays
    /// counted after the worktree switched to another branch.
    /// </remarks>
    /// <param name="path">The worktree folder.</param>
    /// <param name="branch">The branch the worktree was made with, which removing it may delete.</param>
    /// <param name="cancellationToken">A token that stops the work.</param>
    /// <returns>The state, or null when the folder is missing or git failed.</returns>
    public async Task<GitWorktreeState?> InspectAsync(string path, string branch, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!Directory.Exists(path) || !GitBranchName.IsValid(branch))
        {
            return null;
        }

        var status = await _git.RunAsync(path, ["status", "--porcelain", "--untracked-files=all"], cancellationToken);
        var exists = await GitBranches.ExistsAsync(_git, path, branch, cancellationToken);
        if (!status.Succeeded)
        {
            return null;
        }

        // HEAD on another branch cancels out against --branches; detached, its own commits count.
        List<string> tips = exists ? ["HEAD", "refs/heads/" + branch] : ["HEAD"];
        var unshared = await _git.RunAsync(path, ["rev-list", "--count", .. tips, "--not", "--exclude=" + branch, "--branches", "--remotes"], cancellationToken);
        if (!unshared.Succeeded || !int.TryParse(unshared.Output.Trim(), out var commits))
        {
            return null;
        }

        var files = status.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        return new GitWorktreeState(files, commits);
    }

    /// <summary>
    /// Reconnect a worktree to its repository after the repository moved.
    /// </summary>
    /// <param name="folder">A folder inside the repository's new location.</param>
    /// <param name="path">The worktree folder.</param>
    /// <param name="cancellationToken">A token that stops the work.</param>
    /// <returns>The repository's new shared git folder, or null when git failed.</returns>
    public async Task<string?> RepairAsync(string folder, string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentException.ThrowIfNullOrEmpty(path);
        var repaired = await _git.RunAsync(folder, ["worktree", "repair", path], cancellationToken);
        var common = await _git.RunAsync(folder, ["rev-parse", "--path-format=absolute", "--git-common-dir"], cancellationToken);
        return repaired.Succeeded && common.Succeeded ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(common.Output.Trim())) : null;
    }

    /// <summary>
    /// Remove a worktree with any changes in it, and optionally its branch.
    /// </summary>
    /// <param name="folder">A folder inside the main working tree.</param>
    /// <param name="path">The worktree folder.</param>
    /// <param name="branch">The worktree's branch.</param>
    /// <param name="deleteBranch">Whether to delete the branch too, with any commits only it has.</param>
    /// <param name="cancellationToken">A token that stops the work.</param>
    /// <returns>True when the worktree folder is gone and git no longer lists it.</returns>
    public async Task<bool> RemoveAsync(string folder, string path, string branch, bool deleteBranch, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentException.ThrowIfNullOrEmpty(path);

        var removed = !Directory.Exists(path)
            || (await _git.RunAsync(folder, ["worktree", "remove", "--force", path], cancellationToken, CheckoutTimeout)).Succeeded;
        await _git.RunAsync(folder, ["worktree", "prune"], cancellationToken);
        if (removed && deleteBranch && GitBranchName.IsValid(branch))
        {
            await _git.RunAsync(folder, ["branch", "--delete", "--force", branch], cancellationToken);
        }

        return removed;
    }
}
