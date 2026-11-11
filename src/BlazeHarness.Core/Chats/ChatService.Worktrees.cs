using BlazeHarness.Core.Folders;
using BlazeHarness.Core.Git;
using Microsoft.Extensions.Logging;

namespace BlazeHarness.Core.Chats;

/// <content>
/// Moves folder chats into git worktrees of their own and back.
/// </content>
public sealed partial class ChatService
{
    /// <summary>
    /// Create a branch in a new worktree under the chat's own folder and move the chat into it.
    /// </summary>
    /// <param name="id">The chat id; the chat must work in a folder, not be in a worktree already, and not be replying.</param>
    /// <param name="branch">The new branch name.</param>
    /// <param name="baseBranch">The local branch to start from.</param>
    /// <param name="cancellationToken">A token that stops the work.</param>
    /// <returns>What happened.</returns>
    public Task<GitBranchResult> MoveToWorktreeAsync(string id, string branch, string baseBranch, CancellationToken cancellationToken = default)
    {
        return WhileIdleAsync(id, GitBranchResult.NotAllowed, async () =>
        {
            if (_store.Find(id) is not { FolderPath: { } folder, Worktree: null })
            {
                return GitBranchResult.NotAllowed;
            }

            var path = _store.Paths.ChatWorktreeDirectory(id);
            var (result, worktree) = await _worktrees.AddAsync(folder, path, branch, baseBranch, cancellationToken);
            if (worktree is null)
            {
                return result;
            }

            try
            {
                await _store.UpdateAsync(id, s => s with { FolderPath = worktree.FolderPath, Worktree = new ChatWorktree(folder, branch, worktree.GitDirectory) }, cancellationToken: CancellationToken.None);
            }
            catch
            {
                // A worktree the chat doesn't record would never be cleaned up.
                await _worktrees.RemoveAsync(folder, path, branch, deleteBranch: true, CancellationToken.None);
                throw;
            }

            await RefreshProjectsAsync(CancellationToken.None);
            return GitBranchResult.Done;
        });
    }

    /// <summary>
    /// Count the work in a chat's worktree that removing it would lose.
    /// </summary>
    /// <param name="id">The chat id.</param>
    /// <param name="cancellationToken">A token that stops the work.</param>
    /// <returns>The state, or null when the chat has no worktree or git could not read it.</returns>
    public Task<GitWorktreeState?> InspectWorktreeAsync(string id, CancellationToken cancellationToken = default)
    {
        return _store.Find(id)?.Worktree is { } worktree
            ? _worktrees.InspectAsync(_store.Paths.ChatWorktreeDirectory(id), worktree.Branch, cancellationToken)
            : Task.FromResult<GitWorktreeState?>(null);
    }

    /// <summary>
    /// Remove a chat's worktree, with any uncommitted changes in it, and move the chat back to the folder it came from.
    /// </summary>
    /// <param name="id">The chat id; the chat must not be replying.</param>
    /// <param name="deleteBranch">Whether to delete the worktree's branch too.</param>
    /// <returns>False when the chat is replying, has no worktree, or the worktree could not be removed.</returns>
    public Task<bool> LeaveWorktreeAsync(string id, bool deleteBranch)
    {
        return WhileIdleAsync(id, false, async () =>
        {
            if (_store.Find(id) is not { Worktree: { } worktree } || !await RemoveWorktreeAsync(id, worktree, deleteBranch))
            {
                return false;
            }

            await _store.UpdateAsync(id, s => s with { FolderPath = worktree.RepositoryPath, Worktree = null }, cancellationToken: CancellationToken.None);
            await RefreshProjectsAsync(CancellationToken.None);
            return true;
        });
    }

    /// <summary>
    /// Move every chat in a folder that moved to its new location, and reconnect the worktrees of chats from it.
    /// </summary>
    /// <param name="oldPath">The folder's old path.</param>
    /// <param name="newPath">The folder's new path.</param>
    /// <returns>The number of chats that were changed.</returns>
    public async Task<int> RelocateFolderAsync(string oldPath, string newPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(newPath);
        var moved = await _store.RepointFolderAsync(oldPath, newPath, CancellationToken.None);
        foreach (var id in _store.List().Where(c => c.Worktree is { } w && FolderPaths.Comparer.Equals(w.RepositoryPath, newPath)).Select(c => c.Id))
        {
            // Git keeps absolute links both ways between a repository and its worktrees, so a moved repository breaks them.
            if (await _worktrees.RepairAsync(newPath, _store.Paths.ChatWorktreeDirectory(id), CancellationToken.None) is { } gitDirectory)
            {
                await _store.UpdateAsync(id, s => s.Worktree is { } w ? s with { Worktree = w with { GitDirectory = gitDirectory } } : s, cancellationToken: CancellationToken.None);
            }
            else
            {
                LogWorktreeNotRepaired(id);
            }
        }

        await RefreshProjectsAsync(CancellationToken.None);
        return moved;
    }

    /// <summary>
    /// Run a change to a chat's folder while no reply can start, and no other such change or delete can run.
    /// </summary>
    /// <remarks>
    /// The open guard keeps a chat that isn't loaded from being restored meanwhile; a loaded one is held.
    /// </remarks>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="id">The chat id.</param>
    /// <param name="busy">The result when the chat is replying or busy with another change.</param>
    /// <param name="work">The change.</param>
    /// <returns>The change's result, or <paramref name="busy"/>.</returns>
    private async Task<T> WhileIdleAsync<T>(string id, T busy, Func<Task<T>> work)
    {
        using (await _opening.EnterAsync(id, CancellationToken.None))
        {
            var chat = _live.TryGetValue(id, out var live) ? live : null;
            if (chat is not null && !chat.TryHold())
            {
                return busy;
            }

            try
            {
                return await work();
            }
            finally
            {
                chat?.Release();
            }
        }
    }

    /// <summary>
    /// Remove a chat's worktree, stopping its background commands first so no process holds files in it.
    /// </summary>
    /// <param name="id">The chat id.</param>
    /// <param name="worktree">The chat's worktree.</param>
    /// <param name="deleteBranch">Whether to delete the worktree's branch too.</param>
    /// <returns>True when the worktree folder is gone.</returns>
    private async Task<bool> RemoveWorktreeAsync(string id, ChatWorktree worktree, bool deleteBranch)
    {
        if (_live.TryGetValue(id, out var chat))
        {
            foreach (var job in chat.BackgroundShells.Jobs)
            {
                chat.BackgroundShells.Stop(job.Id);
            }
        }

        var path = _store.Paths.ChatWorktreeDirectory(id);
        var repository = Directory.Exists(worktree.RepositoryPath) ? worktree.RepositoryPath : worktree.GitDirectory;
        if (await _worktrees.RemoveAsync(repository, path, worktree.Branch, deleteBranch, CancellationToken.None))
        {
            return true;
        }

        // Git refuses when the repository is gone or damaged; the folder is the app's own, so delete it and let git forget it.
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogWorktreeNotRemoved(ex, id);
            return false;
        }

        return await _worktrees.RemoveAsync(repository, path, worktree.Branch, deleteBranch, CancellationToken.None);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not reconnect the worktree of chat {ChatId} to its moved repository.")]
    private partial void LogWorktreeNotRepaired(string chatId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not remove the worktree of chat {ChatId}.")]
    private partial void LogWorktreeNotRemoved(Exception ex, string chatId);
}
