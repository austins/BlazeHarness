namespace BlazeHarness.Core.Chats;

/// <summary>
/// The git worktree a chat works in, which the app made for it under the chat's own folder.
/// </summary>
/// <param name="RepositoryPath">The registered folder the worktree was made from; the chat is listed under it.</param>
/// <param name="Branch">The branch the worktree was made with.</param>
/// <param name="GitDirectory">The repository's shared git folder, recorded when the worktree was made, which the shell sandbox lets git reach.</param>
public sealed record ChatWorktree(string RepositoryPath, string Branch, string GitDirectory);
