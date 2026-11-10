namespace BlazeHarness.Core.Git;

/// <summary>
/// Lists, switches and creates the local branches of a repository.
/// </summary>
/// <remarks>
/// Switching changes the user's files, so it must only run when the user asked for it. Every name is checked with
/// <see cref="GitBranchName.IsValid"/> first, so none can be read as an option.
/// </remarks>
public sealed class GitBranches
{
    private static readonly TimeSpan CheckoutTimeout = TimeSpan.FromMinutes(5);

    private readonly IGitRunner _git;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitBranches"/> class.
    /// </summary>
    /// <param name="time">The clock that times out a slow git program.</param>
    public GitBranches(TimeProvider time)
        : this(new GitProcessRunner(time))
    {
    }

    internal GitBranches(IGitRunner git)
    {
        _git = git;
    }

    /// <summary>
    /// Read the branches as a folder's working tree sees them.
    /// </summary>
    /// <param name="folder">A folder inside the working tree.</param>
    /// <param name="cancellationToken">A token that stops the work.</param>
    /// <returns>The branches, or null when the folder is not in a git repository or git failed.</returns>
    public async Task<GitBranchList?> ListAsync(string folder, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        if (!Directory.Exists(folder))
        {
            return null;
        }

        var head = await _git.RunAsync(folder, ["symbolic-ref", "--quiet", "--short", "HEAD"], cancellationToken);
        string? current = null;
        string? detachedAt = null;
        if (head.Succeeded)
        {
            current = head.Output.Trim();
        }
        else if (head.ExitCode == 1)
        {
            var commit = await _git.RunAsync(folder, ["rev-parse", "--short", "HEAD"], cancellationToken);
            if (!commit.Succeeded)
            {
                return null;
            }

            detachedAt = commit.Output.Trim();
        }
        else
        {
            return null;
        }

        var refs = await _git.RunAsync(folder, ["for-each-ref", "--format=%(refname:short)%00%(worktreepath)", "refs/heads"], cancellationToken);
        var status = await _git.RunAsync(folder, ["status", "--porcelain", "--untracked-files=no"], cancellationToken);
        if (!refs.Succeeded || !status.Succeeded)
        {
            return null;
        }

        var branches = refs.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\0'))
            .Select(parts => new GitBranch(parts[0], parts.Length > 1 && parts[1].Length > 0 ? Path.GetFullPath(parts[1]) : null))
            .OrderBy(branch => branch.Name, StringComparer.Ordinal)
            .ToList();
        return new GitBranchList(current, detachedAt, branches, status.Output.Length > 0);
    }

    /// <summary>
    /// Check out another local branch in a folder's working tree.
    /// </summary>
    /// <param name="folder">A folder inside the working tree.</param>
    /// <param name="name">The branch to check out.</param>
    /// <param name="cancellationToken">A token that stops the work.</param>
    /// <returns>What happened; <see cref="GitBranchResult.NotAllowed"/> when tracked files have uncommitted changes.</returns>
    public async Task<GitBranchResult> SwitchAsync(string folder, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        if (!GitBranchName.IsValid(name))
        {
            return GitBranchResult.InvalidName;
        }

        if (await ListAsync(folder, cancellationToken) is not { } list)
        {
            return GitBranchResult.Failed;
        }

        if (list.HasChanges || !list.Branches.Any(branch => branch.Name == name && branch.WorktreePath is null))
        {
            return GitBranchResult.NotAllowed;
        }

        var result = await _git.RunAsync(folder, ["switch", "--no-guess", name], cancellationToken, CheckoutTimeout);
        return result.Succeeded ? GitBranchResult.Done : GitBranchResult.Failed;
    }

    /// <summary>
    /// Create a branch from the current commit and check it out, keeping any uncommitted changes.
    /// </summary>
    /// <param name="folder">A folder inside the working tree.</param>
    /// <param name="name">The new branch name.</param>
    /// <param name="cancellationToken">A token that stops the work.</param>
    /// <returns>What happened.</returns>
    public async Task<GitBranchResult> CreateAsync(string folder, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        if (!GitBranchName.IsValid(name))
        {
            return GitBranchResult.InvalidName;
        }

        if (await ExistsAsync(_git, folder, name, cancellationToken))
        {
            return GitBranchResult.Exists;
        }

        var result = await _git.RunAsync(folder, ["switch", "--create", name], cancellationToken, CheckoutTimeout);
        return result.Succeeded ? GitBranchResult.Done : GitBranchResult.Failed;
    }

    /// <summary>
    /// Get whether a local branch exists.
    /// </summary>
    /// <param name="git">Runs git.</param>
    /// <param name="folder">A folder inside the repository.</param>
    /// <param name="name">A name that passed <see cref="GitBranchName.IsValid"/>.</param>
    /// <param name="cancellationToken">A token that stops the work.</param>
    /// <returns>True when the branch exists.</returns>
    internal static async Task<bool> ExistsAsync(IGitRunner git, string folder, string name, CancellationToken cancellationToken)
    {
        var result = await git.RunAsync(folder, ["show-ref", "--verify", "--quiet", "refs/heads/" + name], cancellationToken);
        return result.Succeeded;
    }
}
