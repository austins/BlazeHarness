using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Attachments;
using BlazeHarness.Core.Chats.Export;
using BlazeHarness.Core.Chats.Titles;
using BlazeHarness.Core.Compaction;
using BlazeHarness.Core.Folders;
using BlazeHarness.Core.Mcp;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Sandbox.Ports;
using BlazeHarness.Core.Skills;
using BlazeHarness.Core.Tools;
using BlazeHarness.Core.Tools.Files;
using BlazeHarness.Core.Tools.Shell;
using BlazeHarness.Core.Web;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace BlazeHarness.Core.Chats;

/// <summary>
/// The app-wide owner of conversations: it keeps each opened <see cref="Chat"/> alive independent of any page,
/// so a reply carries on when the tab that started it is closed and any tab can pick it up again.
/// </summary>
/// <remarks>
/// Chats are restored from their log on first use and then kept in memory until they are deleted.
/// Caller cancellation stops preparation, not accepted replies or committed permission changes and deletions.
/// Shutdown cancels title requests.
/// </remarks>
public sealed partial class ChatService : IDisposable
{
    private const int PlaceholderLength = 48;

    private const int MaxTitleAttempts = 3;
    private const int MaxSearchedChats = 30;

    private readonly ChatStore _store;
    private readonly Func<ChatTarget, CancellationToken, Task<IChatClient>> _clientFactory;
    private readonly ILoggerFactory _loggers;
    private readonly WebServices? _web;
    private readonly TimeProvider _time;
    private readonly ITitleGenerator? _titles;
    private readonly IShellSandbox _sandbox;
    private readonly PermissionDefaults? _defaults;
    private readonly McpManager? _mcp;
    private readonly SkillCatalog? _skills;
    private readonly Func<CompactionMode>? _compactionMode;
    private readonly ILogger _log;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<string, Task> _titling = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _titleFailures = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Chat> _live = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _opening = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatService"/> class.
    /// </summary>
    /// <param name="store">The saved chats.</param>
    /// <param name="clientFactory">Creates the chat client for a target.</param>
    /// <param name="loggers">Creates loggers for the chats.</param>
    /// <param name="time">The clock for timestamps.</param>
    /// <param name="titles">Writes a title after the first exchange; without it a chat keeps its placeholder title.</param>
    /// <param name="sandbox">Runs shell commands; without it commands only run in chats with the Unrestricted access level.</param>
    /// <param name="defaults">The global permission choices; without it new chats start with the built-in settings and the risky options stay available.</param>
    /// <param name="mcp">Provides the tools of MCP servers; without it chats have no MCP tools.</param>
    /// <param name="skills">Finds the skills a chat can use; without it chats have no skills.</param>
    /// <param name="compactionMode">Tells how summaries are written each time a chat is compacted; without it the model writes them.</param>
    /// <param name="web">Provides the web search and fetch tools; without it chats have no web tools.</param>
    public ChatService(
        ChatStore store,
        Func<ChatTarget, CancellationToken, Task<IChatClient>> clientFactory,
        ILoggerFactory loggers,
        TimeProvider time,
        ITitleGenerator? titles = null,
        IShellSandbox? sandbox = null,
        PermissionDefaults? defaults = null,
        McpManager? mcp = null,
        SkillCatalog? skills = null,
        Func<CompactionMode>? compactionMode = null,
        WebServices? web = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(loggers);
        ArgumentNullException.ThrowIfNull(time);
        _store = store;
        _clientFactory = clientFactory;
        _loggers = loggers;
        _time = time;
        _titles = titles;
        _skills = skills;
        _sandbox = sandbox ?? new NoShellSandbox("No sandbox is set up.");
        _defaults = defaults;
        _mcp = mcp;
        _web = web;
        _compactionMode = compactionMode;
        _log = loggers.CreateLogger<ChatService>();
    }

    /// <summary>
    /// Raised, on any thread, when a chat starts or stops replying, or starts or stops waiting for the user.
    /// </summary>
    public event EventHandler? ActivityChanged;

    /// <summary>
    /// Gets the saved chats.
    /// </summary>
    public ChatStore Store => _store;

    /// <summary>
    /// Create a new chat and open it.
    /// </summary>
    /// <param name="folderPath">The folder the chat works in, or null for none.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>The new chat's metadata.</returns>
    public async Task<ChatInfo> CreateAsync(string? folderPath = null, CancellationToken cancellationToken = default)
    {
        var info = await _store.CreateAsync(folderPath, cancellationToken);
        var start = StartingPermissions(folderPath is not null);
        if (start != info.Permissions)
        {
            info = await _store.UpdateAsync(info.Id, s => StoreSettings(s, start), cancellationToken: cancellationToken) ?? info;
        }

        var chat = await NewChatAsync(info.Id);
        chat.Permissions.Settings = info.Permissions;
        _live[info.Id] = chat;
        await ApplyFolderAsync(info.Id, chat, CancellationToken.None);
        return info;
    }

    /// <summary>
    /// Export a chat as a file.
    /// </summary>
    /// <param name="id">The chat id.</param>
    /// <param name="format">The file format.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The file, or null when there is no chat with that id.</returns>
    public async Task<ExportedFile?> ExportAsync(string id, ExportFormat format, CancellationToken cancellationToken = default)
    {
        if (_store.Find(id) is not { } info)
        {
            return null;
        }

        var events = await _store.GetLog(id).ReadAsync(cancellationToken);
        return ChatExport.Export(info, events, format, _time.GetUtcNow());
    }

    /// <summary>
    /// Create a chat from an exported chat file.
    /// </summary>
    /// <remarks>
    /// The new chat has no folder and starts with the default permissions for a chat without a folder,
    /// whatever the file says, so a file can't grant access. The chat can be moved to a folder afterwards.
    /// </remarks>
    /// <param name="json">The text of the exported file.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>The new chat's metadata.</returns>
    /// <exception cref="FormatException">The file is not a chat export this version can read; the message can be shown to the user.</exception>
    public async Task<ChatInfo> ImportAsync(string json, CancellationToken cancellationToken = default)
    {
        if (!ChatImport.TryParse(json, out var imported, out var error) || imported is null)
        {
            throw new FormatException(error);
        }

        var created = await _store.CreateAsync(null, cancellationToken);
        var start = StartingPermissions(false);
        try
        {
            await _store.GetLog(created.Id).AppendAsync(imported.Events, cancellationToken);
            return await _store.UpdateAsync(
                created.Id,
                s => StoreSettings(
                    s with
                    {
                        Title = imported.Info.Title,
                        TitleSource = imported.Info.TitleSource,
                        CreatedAt = imported.Info.CreatedAt,
                        ProviderId = imported.Info.ProviderId,
                        ModelId = imported.Info.ModelId,
                        Level = imported.Info.Level,
                    },
                    start),
                touch: true,
                cancellationToken: cancellationToken) ?? created;
        }
        catch
        {
            await _store.DeleteAsync(created.Id, CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Open a chat, restoring its messages from the log the first time.
    /// </summary>
    /// <param name="id">The chat id.</param>
    /// <param name="cancellationToken">A token to cancel loading the chat.</param>
    /// <returns>The chat, or null when there is none with that id.</returns>
    public async Task<Chat?> OpenAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_store.Find(id) is null)
        {
            return null;
        }

        if (_live.TryGetValue(id, out var live))
        {
            return live;
        }

        await _opening.WaitAsync(cancellationToken);
        try
        {
            if (_live.TryGetValue(id, out live))
            {
                return live;
            }

            Chat? chat = null;
            try
            {
                chat = await NewChatAsync(id);
                chat.Restore(await _store.GetLog(id).ReadAsync(cancellationToken));
                chat.Permissions.Settings = _store.Find(id)?.Permissions ?? PermissionSettings.Default;
                await ApplyFolderAsync(id, chat, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                // The chat may have been deleted while it was being restored.
                if (_store.Find(id) is null)
                {
                    return null;
                }

                _live[id] = chat;
                var opened = chat;
#pragma warning disable IDISP003 // Ownership transferred to the live registry; only failed initialization is disposed here.
                chat = null;
#pragma warning restore IDISP003
                return opened;
            }
            finally
            {
                chat?.Dispose();
            }
        }
        finally
        {
            _opening.Release();
        }
    }

    /// <summary>
    /// Change a chat's access level and approvals, which the chat remembers and logs.
    /// </summary>
    /// <param name="id">The chat id.</param>
    /// <param name="settings">The new settings.</param>
    /// <param name="cancellationToken">A token to cancel before the permission change begins; once begun, both saves finish.</param>
    /// <returns>A task that completes once the change is saved; it does nothing when the chat is unknown.</returns>
    public async Task SetPermissionsAsync(string id, PermissionSettings settings, CancellationToken cancellationToken = default)
    {
        if (await OpenAsync(id, cancellationToken) is not { } chat)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await chat.SetPermissionsAsync(settings);
        await _store.UpdateAsync(id, s => StoreSettings(s, settings), cancellationToken: CancellationToken.None);
    }

    /// <summary>
    /// Get whether an MCP server is on for a chat.
    /// </summary>
    /// <param name="id">The chat id.</param>
    /// <param name="server">The server.</param>
    /// <returns>True when the chat's tools include the server's tools: the chat's own choice if it made one, else the server's global state.</returns>
    public bool IsMcpServerEnabled(string id, McpServerConfig server)
    {
        ArgumentNullException.ThrowIfNull(server);
        return McpManager.IsEnabled(server, _store.Find(id)?.McpServers);
    }

    /// <summary>
    /// Turn an MCP server on or off for one chat, or go back to its global state.
    /// </summary>
    /// <param name="id">The chat id.</param>
    /// <param name="serverName">The server's name in <c>mcp.json</c>.</param>
    /// <param name="enabled">True or false to choose for this chat, or null to follow the global state again.</param>
    /// <param name="cancellationToken">A token to cancel saving the choice.</param>
    /// <returns>A task that completes once the choice is saved. It applies from the chat's next reply.</returns>
    public Task SetMcpServerAsync(string id, string serverName, bool? enabled, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        return _store.UpdateAsync(
            id,
            s =>
            {
                return s with { McpServers = WithChoice(s.McpServers, serverName, enabled) };
            },
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get the skills a chat can use, before any are turned off.
    /// </summary>
    /// <param name="id">The chat id.</param>
    /// <returns>The skills of the chat's folder and of the shared folders, or none when skills are not set up.</returns>
    public SkillResolution AvailableSkills(string id)
    {
        return _skills?.Resolve(_store.Find(id)?.FolderPath) ?? SkillResolution.Of();
    }

    /// <summary>
    /// Get whether a skill is on for a chat.
    /// </summary>
    /// <param name="id">The chat id.</param>
    /// <param name="skillName">The skill's name.</param>
    /// <returns>True when the chat offers the skill: its own choice if it made one, else the global state.</returns>
    public bool IsSkillEnabled(string id, string skillName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillName);
        return SkillCatalog.IsEnabled(skillName, _skills?.IsDisabledGlobally(skillName) ?? false, _store.Find(id)?.Skills);
    }

    /// <summary>
    /// Turn a skill on or off for one chat, or go back to its global state.
    /// </summary>
    /// <param name="id">The chat id.</param>
    /// <param name="skillName">The skill's name.</param>
    /// <param name="enabled">True or false to choose for this chat, or null to follow the global state again.</param>
    /// <param name="cancellationToken">A token to cancel saving the choice.</param>
    /// <returns>A task that completes once the choice is saved. It applies from the chat's next reply.</returns>
    public async Task SetSkillAsync(string id, string skillName, bool? enabled, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillName);
        await _store.UpdateAsync(
            id,
            s =>
            {
                return s with { Skills = WithChoice(s.Skills, skillName, enabled) };
            },
            cancellationToken: cancellationToken);
        if (_live.TryGetValue(id, out var chat))
        {
            ApplySkills(id, chat);
        }
    }

    /// <summary>
    /// Send a message in a chat, and remember the target it was sent with.
    /// </summary>
    /// <param name="id">The chat id.</param>
    /// <param name="text">The message text.</param>
    /// <param name="target">The provider, model and level to use.</param>
    /// <param name="cancellationToken">A token to cancel preparation; an accepted reply is stopped through <see cref="Stop"/>.</param>
    /// <returns>A task that completes when the reply has ended; it does nothing when the chat is unknown, busy, or the text is blank.</returns>
    public Task SendAsync(string id, string text, ChatTarget target, CancellationToken cancellationToken = default) => SendAsync(id, text, [], target, cancellationToken);

    /// <summary>
    /// Send a message with attached files in a chat, and remember the target it was sent with.
    /// </summary>
    /// <param name="id">The chat id.</param>
    /// <param name="text">The message text, which may be blank when files are attached.</param>
    /// <param name="files">The files, already saved with <see cref="SaveAttachmentAsync"/>.</param>
    /// <param name="target">The provider, model and level to use.</param>
    /// <param name="cancellationToken">A token to cancel preparation; an accepted reply is stopped through <see cref="Stop"/>.</param>
    /// <returns>A task that completes when the reply has ended; it does nothing when the chat is unknown, busy, or there is neither text nor a file.</returns>
    public async Task SendAsync(string id, string text, IReadOnlyList<AttachmentInfo> files, ChatTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(target);
        if (await OpenAsync(id, cancellationToken) is not { IsRunning: false } chat || (string.IsNullOrWhiteSpace(text) && files.Count == 0))
        {
            return;
        }

        await RememberAsync(id, target, string.IsNullOrWhiteSpace(text) ? string.Join(", ", files.Select(f => f.Name)) : text, cancellationToken: cancellationToken);
        await ApplyFolderAsync(id, chat, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await chat.SendAsync(text, files, target);
        StartTitle(id, chat, target);
    }

    /// <summary>
    /// Save a file the user attached to a chat.
    /// </summary>
    /// <param name="id">The chat id.</param>
    /// <param name="fileName">The name the client gave the file.</param>
    /// <param name="content">The file content.</param>
    /// <param name="cancellationToken">A token that stops the upload.</param>
    /// <returns>The saved file, or null when the chat is unknown.</returns>
    /// <exception cref="AttachmentException">The file is empty, too large, or neither an image nor text.</exception>
    public async Task<AttachmentInfo?> SaveAttachmentAsync(string id, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        return _store.Find(id) is null
            ? null
            : await new ChatAttachments(_store.AttachmentsDirectory(id)).SaveAsync(fileName, content, cancellationToken);
    }

    /// <summary>
    /// Find a file attached in a chat.
    /// </summary>
    /// <param name="id">The chat id.</param>
    /// <param name="attachmentId">The attachment id.</param>
    /// <returns>The file, or null when the chat or the attachment is unknown.</returns>
    public AttachmentFile? FindAttachment(string id, string attachmentId)
    {
        return _store.Find(id) is null ? null : new ChatAttachments(_store.AttachmentsDirectory(id)).Find(attachmentId);
    }

    /// <summary>
    /// Find an image inside the working folder of a chat, or the chat's own files folder when it has none, so the chat can show it.
    /// </summary>
    /// <param name="id">The chat id.</param>
    /// <param name="path">The path as the model wrote it, relative to the folder or absolute.</param>
    /// <returns>The image, or null when the chat is unknown, the path leaves the folder, or the file is not a supported image within the size cap.</returns>
    public AttachmentFile? FindFolderImage(string id, string path)
    {
        if (_store.Find(id) is not { } chat)
        {
            return null;
        }

        var folder = chat.FolderPath ?? _store.OutputsDirectory(id);
        if (!Directory.Exists(folder))
        {
            return null;
        }

        return ShowImageTool.TryFind(new ToolContext(folder), path, out var fullPath, out var mediaType, out _)
            ? new AttachmentFile(fullPath, Path.GetFileName(fullPath), mediaType)
            : null;
    }

    /// <summary>
    /// Replace one of the user's messages in a chat and stream a new reply, remembering the target it was sent with.
    /// </summary>
    /// <param name="id">The chat id.</param>
    /// <param name="entryId">The id of the message to replace.</param>
    /// <param name="text">The new message text.</param>
    /// <param name="target">The provider, model and level to use.</param>
    /// <param name="cancellationToken">A token to cancel preparation; an accepted reply is stopped through <see cref="Stop"/>.</param>
    /// <returns>A task that completes when the reply has ended; it does nothing when the chat is unknown, busy, the message can't be edited or the text is blank.</returns>
    public async Task EditAsync(string id, Guid entryId, string text, ChatTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (await OpenAsync(id, cancellationToken) is not { IsRunning: false } chat
            || string.IsNullOrWhiteSpace(text)
            || !chat.CanEdit(entryId))
        {
            return;
        }

        var first = chat.Entries is [{ } head, ..] && head.Id == entryId;
        await RememberAsync(id, target, first ? text : null, replacePlaceholder: first, cancellationToken: cancellationToken);
        await ApplyFolderAsync(id, chat, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await chat.EditAsync(entryId, text, target);
        StartTitle(id, chat, target);
    }

    /// <summary>
    /// Retry the last reply of a chat, and remember the target it was retried with.
    /// </summary>
    /// <param name="id">The chat id.</param>
    /// <param name="target">The provider, model and level to use.</param>
    /// <param name="cancellationToken">A token to cancel preparation; an accepted reply is stopped through <see cref="Stop"/>.</param>
    /// <returns>A task that completes when the reply has ended; it does nothing when the chat is unknown or its last reply can't be retried.</returns>
    public async Task RetryAsync(string id, ChatTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (await OpenAsync(id, cancellationToken) is not { CanRetry: true } chat)
        {
            return;
        }

        await RememberAsync(id, target, null, cancellationToken: cancellationToken);
        await ApplyFolderAsync(id, chat, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await chat.RetryAsync(target);
        StartTitle(id, chat, target);
    }

    /// <summary>
    /// Replace the older messages of a chat with a summary.
    /// </summary>
    /// <param name="id">The chat id.</param>
    /// <param name="target">The model that writes the summary.</param>
    /// <param name="cancellationToken">A token to cancel preparation; accepted compaction is stopped through <see cref="Stop"/>.</param>
    /// <returns>True when the chat was compacted; false when it is unknown, busy, has nothing older to summarize, or was stopped.</returns>
    /// <exception cref="InvalidOperationException">The model could not write the summary.</exception>
    public async Task<bool> CompactAsync(string id, ChatTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (await OpenAsync(id, cancellationToken) is not { CanCompact: true } chat)
        {
            return false;
        }

        await ApplyFolderAsync(id, chat, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return await chat.CompactAsync(target);
    }

    /// <summary>
    /// Stop the reply a chat is producing, if any.
    /// </summary>
    /// <param name="id">The chat id.</param>
    public void Stop(string id)
    {
        if (_live.TryGetValue(id, out var chat))
        {
            chat.Stop();
        }
    }

    /// <summary>
    /// Get whether a chat is producing a reply right now.
    /// </summary>
    /// <param name="id">The chat id.</param>
    /// <returns>True when a reply is running.</returns>
    public bool IsRunning(string id)
    {
        return _live.TryGetValue(id, out var chat) && chat.IsRunning;
    }

    /// <summary>
    /// Get whether a chat waits for the user, either to answer a question or to allow a tool call.
    /// </summary>
    /// <param name="id">The chat id.</param>
    /// <returns>True when the chat is waiting for the user.</returns>
    public bool NeedsUser(string id)
    {
        return _live.TryGetValue(id, out var chat) && chat.NeedsUser;
    }

    /// <summary>
    /// Delete a chat and everything stored for it, unless it is producing a reply.
    /// </summary>
    /// <param name="id">The chat id.</param>
    /// <param name="cancellationToken">A token to cancel before deletion begins; once begun, deletion finishes.</param>
    /// <returns>False when the chat is running and was left alone; true otherwise.</returns>
    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_live.TryGetValue(id, out var chat))
        {
            if (!chat.TryClose())
            {
                return false;
            }

            _live.TryRemove(id, out _);
            chat.Dispose();
        }

        _web?.Forget(id);
        await _store.DeleteAsync(id, CancellationToken.None);

        // An open that was restoring the chat meanwhile may have registered it after the check above.
        if (_live.TryRemove(id, out var late))
        {
            late.Dispose();
        }

        return true;
    }

    /// <summary>
    /// Stop every running reply, for when the app shuts down.
    /// </summary>
    public void Dispose()
    {
        _shutdown.Cancel();
        foreach (var chat in _live.Values)
        {
            chat.Dispose();
        }

        _opening.Dispose();
        _shutdown.Dispose();
    }

    /// <summary>
    /// Read the project instructions that apply to a folder.
    /// </summary>
    /// <param name="folderPath">The folder, or null for a chat without one.</param>
    /// <param name="cancellationToken">A token to cancel reading the project instructions.</param>
    /// <returns>The instructions, or null when there is no folder.</returns>
    public static async Task<ProjectContext?> LoadProjectAsync(string? folderPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return folderPath is null || !Directory.Exists(folderPath)
            ? null
            : await ProjectContextLoader.LoadAsync(folderPath, cancellationToken);
    }

    /// <summary>
    /// Re-read the project instructions of every open chat, for after folders were moved, removed or re-pointed.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel refreshing the chats.</param>
    /// <returns>A task that completes when every chat has been updated.</returns>
    public async Task RefreshProjectsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var (id, chat) in _live.ToArray())
        {
            await ApplyFolderAsync(id, chat, cancellationToken);
        }
    }

    /// <summary>
    /// Get a task that completes when the title request of a chat, if one is running, has ended.
    /// </summary>
    /// <param name="id">The chat id.</param>
    /// <returns>The pending task, or a completed one.</returns>
    internal Task PendingTitleAsync(string id)
    {
        return _titling.TryGetValue(id, out var task) ? task : Task.CompletedTask;
    }

    private async Task ApplyFolderAsync(string id, Chat chat, CancellationToken cancellationToken)
    {
        var folderPath = _store.Find(id)?.FolderPath;
        var loaded = await LoadProjectAsync(folderPath, cancellationToken);
        var current = chat.Project;
        if (loaded is null ? current is not null : !loaded.SameAs(current))
        {
            chat.Project = loaded;
        }

        ApplySkills(id, chat);
        ApplyTools(id, chat, folderPath);
    }

    private void ApplySkills(string id, Chat chat)
    {
        if (_skills is null)
        {
            return;
        }

        var info = _store.Find(id);
        var wanted = _skills.For(info?.FolderPath, info?.Skills);
        if (!wanted.SameAs(chat.Skills) && !(wanted.Skills.Count == 0 && chat.Skills is null))
        {
            chat.Skills = wanted;
        }
    }

    private void ApplyTools(string id, Chat chat, string? folderPath)
    {
        if (folderPath is not null && !Directory.Exists(folderPath))
        {
            UseAttachmentsOnly(id, chat, folderPath);
            return;
        }

        var root = folderPath ?? _store.OutputsDirectory(id);
        try
        {
            if (folderPath is null)
            {
                Directory.CreateDirectory(root);
            }

            if (chat.Tools?.Context.Root == RootedPath.CanonicalRoot(root))
            {
                return;
            }

            var shell = ShellInfo.DetectCurrent();
            var tools = BuiltInTools.Create(root, _time, shell, chat.Attachments, _sandbox, _store.ShellDirectory(id), ChatSearchFor(id, chat, folderPath), chat.BackgroundShells);
            chat.UseTools(tools, SystemPrompt.Build(RuntimeInformation.OSDescription, shell?.Name, tools.Context.Root));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LogToolsUnavailable(ex, root);
            UseAttachmentsOnly(id, chat, folderPath);
        }
    }

    private void UseAttachmentsOnly(string id, Chat chat, string? folderPath)
    {
        chat.UseTools(chat.Attachments is { } files ? BuiltInTools.ForAttachments(files, _time, ChatSearchFor(id, chat, folderPath)) : null, null);
    }

    private SearchChatTool ChatSearchFor(string id, Chat chat, string? folderPath)
    {
        return new SearchChatTool(() => chat.Entries, async cancellationToken =>
        {
            List<(string Title, IReadOnlyList<ChatEntry> Entries)> others = [];
            foreach (var other in _store.List().Where(s => s.Id != id && string.Equals(s.FolderPath, folderPath, StringComparison.Ordinal)).Take(MaxSearchedChats))
            {
                var events = await _store.GetLog(other.Id).ReadAsync(cancellationToken);
                others.Add((other.Title, ChatReplay.Replay(events)));
            }

            return others;
        });
    }

    private void StartTitle(string id, Chat chat, ChatTarget target)
    {
        var entries = chat.Entries;
        if (_titles is null
            || _titleFailures.GetValueOrDefault(id) >= MaxTitleAttempts
            || _store.Find(id) is not { TitleSource: TitleSource.Placeholder }
            || entries is not [var first, .., { Status: EntryStatus.Done } last]
            || first.Role != ChatRole.User
            || last.Role != ChatRole.Assistant
            || entries.FirstOrDefault(e => e.Role == ChatRole.Assistant && e.Text.Length > 0) is not { } reply)
        {
            return;
        }

        var gate = new TaskCompletionSource();
        if (!_titling.TryAdd(id, gate.Task))
        {
            return;
        }

        _ = Task.Run(
            async () =>
            {
                try
                {
                    if (await _titles.GenerateAsync(target, first.Text.Length > 0 ? first.Text : string.Join(", ", first.Attachments.Select(a => a.File.Name)), reply.Text, _shutdown.Token) is { } title)
                    {
                        await _store.UpdateAsync(
                            id,
                            s => s.TitleSource == TitleSource.Placeholder ? s with { Title = title, TitleSource = TitleSource.Generated } : s,
                            cancellationToken: _shutdown.Token);
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _titleFailures.AddOrUpdate(id, 1, (_, count) => count + 1);
                    LogTitleFailed(ex);
                }
                finally
                {
                    _titling.TryRemove(id, out _);
                    gate.SetResult();
                }
            },
            CancellationToken.None);
    }

    private static Dictionary<string, bool>? WithChoice(IReadOnlyDictionary<string, bool>? choices, string name, bool? enabled)
    {
        var chosen = (choices ?? new Dictionary<string, bool>()).Where(pair => !string.Equals(pair.Key, name, StringComparison.Ordinal)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (enabled is { } value)
        {
            chosen[name] = value;
        }

        return chosen.Count == 0 ? null : chosen;
    }

    private static ChatInfo StoreSettings(ChatInfo info, PermissionSettings settings)
    {
        return info with
        {
            Access = settings.Access == PermissionSettings.Default.Access ? null : settings.Access,
            Approvals = settings.Approvals == PermissionSettings.Default.Approvals ? null : settings.Approvals,
        };
    }

    private PermissionSettings StartingPermissions(bool hasFolder)
    {
        return _defaults?.For(hasFolder) ?? (hasFolder ? PermissionSettings.Default : PermissionSettings.Default with { Access = AccessLevel.ReadOnly });
    }

    private async Task<Chat> NewChatAsync(string id)
    {
        var chat = new Chat(_clientFactory, _time, _store.GetLog(id), _loggers.CreateLogger<Chat>())
        {
            Attachments = new ChatAttachments(_store.AttachmentsDirectory(id)),
            CompactionModeSource = _compactionMode,
        };
        chat.Permissions.Sensitive = () => _defaults?.Sensitive(id) ?? SensitivePaths.Default(_store.Paths, id);
        chat.Permissions.ReadableFolders = () => [_store.AttachmentsDirectory(id)];
        chat.Permissions.Sandbox = await Task.Run(() => _sandbox.Status, CancellationToken.None);
        if (chat.Permissions.Sandbox.ForwardsPorts && ForwardingTools.Find() is { } forwarding)
        {
#pragma warning disable CA2000, IDISP003 // The chat's background commands own the forwarding and dispose it with the chat; a new chat has none yet.
            chat.BackgroundShells.Ports = new SandboxPorts(forwarding, _time, _loggers.CreateLogger<SandboxPorts>());
#pragma warning restore CA2000, IDISP003
        }

        if (_defaults is { } defaults)
        {
            chat.Permissions.BypassAllowed = () => defaults.AllowBypass;
            chat.Permissions.Rules = () => defaults.Rules;
        }

        var web = _web;
        var mcp = _mcp;
        if (web is not null || mcp is not null)
        {
            chat.ExtraTools = async cancellation =>
            {
                // A sandboxed command's server is only reachable from the browser when its ports are forwarded.
                var browser = chat.Permissions.Settings.Access == AccessLevel.Unrestricted || chat.Permissions.Sandbox.ForwardsPorts;
                List<ITool> tools = [.. web?.ToolsFor(id, browser) ?? []];
                if (mcp is not null)
                {
                    var overrides = _store.Find(id)?.McpServers;
                    var wanted = mcp.Statuses().Where(status => McpManager.IsEnabled(status.Config, overrides)).Select(status => status.Config.Name);
                    tools.AddRange(await mcp.OfferAsync(wanted, cancellation));
                }

                return tools;
            };
        }

        if (mcp is not null && _defaults is { } trust)
        {
            chat.Permissions.TrustedMcpServer = trust.IsMcpServerTrusted;
        }

        var wasActive = (Running: false, NeedsUser: false);
        var activityGate = new Lock();
        chat.Changed += (_, _) =>
        {
            bool flipped;
            lock (activityGate)
            {
                var active = (chat.IsRunning, chat.NeedsUser);
                flipped = active != wasActive;
                wasActive = active;
            }

            if (flipped)
            {
                ActivityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        return chat;
    }

    private Task RememberAsync(string id, ChatTarget target, string? firstText, bool replacePlaceholder = false, CancellationToken cancellationToken = default)
    {
        return _store.UpdateAsync(
            id,
            s => s with
            {
                ProviderId = target.Provider.Id,
                ModelId = target.Model.Id,
                Level = target.Level,
                Title = firstText is not null && (s.Title.Length == 0 || (replacePlaceholder && s.TitleSource == TitleSource.Placeholder)) ? Placeholder(firstText) : s.Title,
            },
            touch: true,
            cancellationToken: cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not write a title for a chat; it keeps its placeholder title.")]
    private partial void LogTitleFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not set up tools for the folder {Folder}; the chat has none.")]
    private partial void LogToolsUnavailable(Exception ex, string folder);

    private static string Placeholder(string text)
    {
        var words = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (words.Length <= PlaceholderLength)
        {
            return words;
        }

        var cut = char.IsHighSurrogate(words[PlaceholderLength - 1]) ? PlaceholderLength - 1 : PlaceholderLength;
        return words[..cut].TrimEnd() + "…";
    }
}
