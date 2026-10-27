using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Attachments;
using BlazeHarness.Core.Folders;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace BlazeHarness.Core.Sessions;

/// <summary>
/// The app-wide owner of conversations: it keeps each opened <see cref="ChatSession"/> alive independent of any page,
/// so a reply carries on when the tab that started it is closed and any tab can pick it up again.
/// </summary>
/// <remarks>
/// Sessions are restored from their log on first use and then kept in memory until they are deleted.
/// </remarks>
[SuppressMessage("Major Code Smell", "S8949", Justification = "The shutdown token only stops title requests; the other calls are short store writes that should finish.")]
public sealed partial class SessionService : IDisposable
{
    private const int PlaceholderLength = 48;

    private readonly SessionStore _store;
    private readonly Func<ChatTarget, CancellationToken, Task<IChatClient>> _clientFactory;
    private readonly ILoggerFactory _loggers;
    private readonly TimeProvider _time;
    private readonly ITitleGenerator? _titles;
    private readonly IShellSandbox _sandbox;
    private readonly PermissionDefaults? _defaults;
    private readonly ILogger _log;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<string, Task> _titling = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ChatSession> _live = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _opening = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionService"/> class.
    /// </summary>
    /// <param name="store">The saved sessions.</param>
    /// <param name="clientFactory">Creates the chat client for a target.</param>
    /// <param name="loggers">Creates loggers for the sessions.</param>
    /// <param name="time">The clock for timestamps; defaults to the system clock.</param>
    /// <param name="titles">Writes a title after the first exchange; without it a session keeps its placeholder title.</param>
    /// <param name="sandbox">Runs shell commands; without it commands only run in sessions with the Unrestricted access level.</param>
    /// <param name="defaults">The global permission choices; without it new chats start with the built-in settings and the risky options stay available.</param>
    public SessionService(
        SessionStore store,
        Func<ChatTarget, CancellationToken, Task<IChatClient>> clientFactory,
        ILoggerFactory loggers,
        TimeProvider? time = null,
        ITitleGenerator? titles = null,
        IShellSandbox? sandbox = null,
        PermissionDefaults? defaults = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(loggers);
        _store = store;
        _clientFactory = clientFactory;
        _loggers = loggers;
        _time = time ?? TimeProvider.System;
        _titles = titles;
        _sandbox = sandbox ?? new NoShellSandbox("No sandbox is set up.");
        _defaults = defaults;
        _log = loggers.CreateLogger<SessionService>();
    }

    /// <summary>
    /// Raised, on any thread, when a session starts or stops replying.
    /// </summary>
    public event EventHandler? ActivityChanged;

    /// <summary>
    /// Gets the saved sessions.
    /// </summary>
    public SessionStore Store => _store;

    /// <summary>
    /// Create a new session and open it.
    /// </summary>
    /// <param name="folderPath">The folder the session works in, or null for none.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>The new session's metadata.</returns>
    public async Task<SessionInfo> CreateAsync(string? folderPath = null, CancellationToken cancellationToken = default)
    {
        var info = await _store.CreateAsync(folderPath, cancellationToken).ConfigureAwait(false);
        var start = StartingPermissions(folderPath is not null);
        if (start != info.Permissions)
        {
            info = await _store.UpdateAsync(info.Id, s => StoreSettings(s, start), cancellationToken: cancellationToken).ConfigureAwait(false) ?? info;
        }

        var session = NewSession(info.Id);
        session.Permissions.Settings = info.Permissions;
        _live[info.Id] = session;
        await ApplyFolderAsync(info.Id, session).ConfigureAwait(false);
        return info;
    }

    /// <summary>
    /// Open a session, restoring its messages from the log the first time.
    /// </summary>
    /// <param name="id">The session id.</param>
    /// <returns>The session, or null when there is none with that id.</returns>
    public async Task<ChatSession?> OpenAsync(string id)
    {
        if (_store.Find(id) is null)
        {
            return null;
        }

        if (_live.TryGetValue(id, out var live))
        {
            return live;
        }

        await _opening.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_live.TryGetValue(id, out live))
            {
                return live;
            }

#pragma warning disable CA2000 // The live registry owns the session once it is added.
            var session = NewSession(id);
#pragma warning restore CA2000
            session.Restore(await _store.GetLog(id).ReadAsync().ConfigureAwait(false));
            session.Permissions.Settings = _store.Find(id)?.Permissions ?? PermissionSettings.Default;
            await ApplyFolderAsync(id, session).ConfigureAwait(false);
            _live[id] = session;
            return session;
        }
        finally
        {
            _opening.Release();
        }
    }

    /// <summary>
    /// Change a session's access level and approvals, which the session remembers and logs.
    /// </summary>
    /// <param name="id">The session id.</param>
    /// <param name="settings">The new settings.</param>
    /// <returns>A task that completes once the change is saved; it does nothing when the session is unknown.</returns>
    public async Task SetPermissionsAsync(string id, PermissionSettings settings)
    {
        if (await OpenAsync(id).ConfigureAwait(false) is not { } session)
        {
            return;
        }

        await session.SetPermissionsAsync(settings).ConfigureAwait(false);
        await _store.UpdateAsync(id, s => StoreSettings(s, settings)).ConfigureAwait(false);
    }

    /// <summary>
    /// Send a message in a session, and remember the target it was sent with.
    /// </summary>
    /// <param name="id">The session id.</param>
    /// <param name="text">The message text.</param>
    /// <param name="target">The provider, model and level to use.</param>
    /// <returns>A task that completes when the reply has ended; it does nothing when the session is unknown, busy, or the text is blank.</returns>
    public Task SendAsync(string id, string text, ChatTarget target) => SendAsync(id, text, [], target);

    /// <summary>
    /// Send a message with attached files in a session, and remember the target it was sent with.
    /// </summary>
    /// <param name="id">The session id.</param>
    /// <param name="text">The message text, which may be blank when files are attached.</param>
    /// <param name="files">The files, already saved with <see cref="SaveAttachmentAsync"/>.</param>
    /// <param name="target">The provider, model and level to use.</param>
    /// <returns>A task that completes when the reply has ended; it does nothing when the session is unknown, busy, or there is neither text nor a file.</returns>
    public async Task SendAsync(string id, string text, IReadOnlyList<AttachmentInfo> files, ChatTarget target)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(target);
        if (await OpenAsync(id).ConfigureAwait(false) is not { IsRunning: false } session || (string.IsNullOrWhiteSpace(text) && files.Count == 0))
        {
            return;
        }

        await RememberAsync(id, target, string.IsNullOrWhiteSpace(text) ? string.Join(", ", files.Select(f => f.Name)) : text).ConfigureAwait(false);
        await ApplyFolderAsync(id, session).ConfigureAwait(false);
        await session.SendAsync(text, files, target).ConfigureAwait(false);
        StartTitle(id, session, target);
    }

    /// <summary>
    /// Save a file the user attached to a session.
    /// </summary>
    /// <param name="id">The session id.</param>
    /// <param name="fileName">The name the client gave the file.</param>
    /// <param name="content">The file content.</param>
    /// <param name="cancellationToken">A token that stops the upload.</param>
    /// <returns>The saved file, or null when the session is unknown.</returns>
    /// <exception cref="AttachmentException">The file is empty, too large, or neither an image nor text.</exception>
    public async Task<AttachmentInfo?> SaveAttachmentAsync(string id, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        return _store.Find(id) is null
            ? null
            : await new SessionAttachments(_store.AttachmentsDirectory(id)).SaveAsync(fileName, content, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Find a file attached in a session.
    /// </summary>
    /// <param name="id">The session id.</param>
    /// <param name="attachmentId">The attachment id.</param>
    /// <returns>The file, or null when the session or the attachment is unknown.</returns>
    public AttachmentFile? FindAttachment(string id, string attachmentId)
    {
        return _store.Find(id) is null ? null : new SessionAttachments(_store.AttachmentsDirectory(id)).Find(attachmentId);
    }

    /// <summary>
    /// Find an image inside the working folder of a session, so the chat can show it.
    /// </summary>
    /// <param name="id">The session id.</param>
    /// <param name="path">The path as the model wrote it, relative to the folder or absolute.</param>
    /// <returns>The image, or null when the session has no folder, the path leaves the folder, or the file is not a supported image within the size cap.</returns>
    public AttachmentFile? FindFolderImage(string id, string path)
    {
        if (_store.Find(id)?.FolderPath is not { } folder || !Directory.Exists(folder))
        {
            return null;
        }

        return ShowImageTool.TryFind(new ToolContext(folder), path, out var fullPath, out var mediaType, out _)
            ? new AttachmentFile(fullPath, Path.GetFileName(fullPath), mediaType)
            : null;
    }

    /// <summary>
    /// Replace one of the user's messages in a session and stream a new reply, remembering the target it was sent with.
    /// </summary>
    /// <param name="id">The session id.</param>
    /// <param name="entryId">The id of the message to replace.</param>
    /// <param name="text">The new message text.</param>
    /// <param name="target">The provider, model and level to use.</param>
    /// <returns>A task that completes when the reply has ended; it does nothing when the session is unknown, busy, the message can't be edited or the text is blank.</returns>
    public async Task EditAsync(string id, Guid entryId, string text, ChatTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (await OpenAsync(id).ConfigureAwait(false) is not { IsRunning: false } session
            || string.IsNullOrWhiteSpace(text)
            || !session.CanEdit(entryId))
        {
            return;
        }

        var first = session.Entries is [{ } head, ..] && head.Id == entryId;
        await RememberAsync(id, target, first ? text : null, replacePlaceholder: first).ConfigureAwait(false);
        await ApplyFolderAsync(id, session).ConfigureAwait(false);
        await session.EditAsync(entryId, text, target).ConfigureAwait(false);
        StartTitle(id, session, target);
    }

    /// <summary>
    /// Retry the last reply of a session, and remember the target it was retried with.
    /// </summary>
    /// <param name="id">The session id.</param>
    /// <param name="target">The provider, model and level to use.</param>
    /// <returns>A task that completes when the reply has ended; it does nothing when the session is unknown or its last reply can't be retried.</returns>
    public async Task RetryAsync(string id, ChatTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (await OpenAsync(id).ConfigureAwait(false) is not { CanRetry: true } session)
        {
            return;
        }

        await RememberAsync(id, target, null).ConfigureAwait(false);
        await ApplyFolderAsync(id, session).ConfigureAwait(false);
        await session.RetryAsync(target).ConfigureAwait(false);
        StartTitle(id, session, target);
    }

    /// <summary>
    /// Replace the older messages of a session with a summary.
    /// </summary>
    /// <param name="id">The session id.</param>
    /// <param name="target">The model that writes the summary.</param>
    /// <returns>True when the session was compacted; false when it is unknown, busy, has nothing older to summarize, or was stopped.</returns>
    /// <exception cref="InvalidOperationException">The model could not write the summary.</exception>
    public async Task<bool> CompactAsync(string id, ChatTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (await OpenAsync(id).ConfigureAwait(false) is not { CanCompact: true } session)
        {
            return false;
        }

        await ApplyFolderAsync(id, session).ConfigureAwait(false);
        return await session.CompactAsync(target).ConfigureAwait(false);
    }

    /// <summary>
    /// Stop the reply a session is producing, if any.
    /// </summary>
    /// <param name="id">The session id.</param>
    public void Stop(string id)
    {
        if (_live.TryGetValue(id, out var session))
        {
            session.Stop();
        }
    }

    /// <summary>
    /// Get whether a session is producing a reply right now.
    /// </summary>
    /// <param name="id">The session id.</param>
    /// <returns>True when a reply is running.</returns>
    public bool IsRunning(string id)
    {
        return _live.TryGetValue(id, out var session) && session.IsRunning;
    }

    /// <summary>
    /// Delete a session and everything stored for it, unless it is producing a reply.
    /// </summary>
    /// <param name="id">The session id.</param>
    /// <returns>False when the session is running and was left alone; true otherwise.</returns>
    public async Task<bool> DeleteAsync(string id)
    {
        if (IsRunning(id))
        {
            return false;
        }

        if (_live.TryRemove(id, out var session))
        {
            session.Dispose();
        }

        await _store.DeleteAsync(id).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Stop every running reply, for when the app shuts down.
    /// </summary>
    public void Dispose()
    {
        _shutdown.Cancel();
        foreach (var session in _live.Values)
        {
            session.Dispose();
        }

        _opening.Dispose();
        _shutdown.Dispose();
    }

    /// <summary>
    /// Read the project instructions that apply to a folder.
    /// </summary>
    /// <param name="folderPath">The folder, or null for a chat without one.</param>
    /// <returns>The instructions, or null when there is no folder.</returns>
    public static async Task<ProjectContext?> LoadProjectAsync(string? folderPath)
    {
        return folderPath is null || !Directory.Exists(folderPath)
            ? null
            : await ProjectContextLoader.LoadAsync(folderPath).ConfigureAwait(false);
    }

    /// <summary>
    /// Re-read the project instructions of every open session, for after folders were moved, removed or re-pointed.
    /// </summary>
    /// <returns>A task that completes when every session has been updated.</returns>
    public async Task RefreshProjectsAsync()
    {
        foreach (var (id, session) in _live.ToArray())
        {
            await ApplyFolderAsync(id, session).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Get a task that completes when the title request of a session, if one is running, has ended.
    /// </summary>
    /// <param name="id">The session id.</param>
    /// <returns>The pending task, or a completed one.</returns>
    internal Task PendingTitleAsync(string id)
    {
        return _titling.TryGetValue(id, out var task) ? task : Task.CompletedTask;
    }

    private async Task ApplyFolderAsync(string id, ChatSession session)
    {
        var folderPath = _store.Find(id)?.FolderPath;
        var loaded = await LoadProjectAsync(folderPath).ConfigureAwait(false);
        var current = session.Project;
        if (loaded is null ? current is not null : !loaded.SameAs(current))
        {
            session.Project = loaded;
        }

        ApplyTools(id, session, folderPath);
    }

    private void ApplyTools(string id, ChatSession session, string? folderPath)
    {
        if (folderPath is not null && !Directory.Exists(folderPath))
        {
            UseAttachmentsOnly(session);
            return;
        }

        var root = folderPath ?? _store.OutputsDirectory(id);
        try
        {
            if (folderPath is null)
            {
                Directory.CreateDirectory(root);
            }

            var shell = ShellInfo.DetectCurrent();
            var tools = BuiltInTools.Create(root, shell, session.Attachments, _sandbox, _store.ShellDirectory(id));
            if (session.Tools?.Context.Root != tools.Context.Root)
            {
                session.Tools = tools;
                session.BasePrompt = SystemPrompt.Build(RuntimeInformation.OSDescription, shell?.Name, tools.Context.Root);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LogToolsUnavailable(ex, root);
            UseAttachmentsOnly(session);
        }
    }

    private static void UseAttachmentsOnly(ChatSession session)
    {
        session.Tools = session.Attachments is { } files ? BuiltInTools.ForAttachments(files) : null;
        session.BasePrompt = null;
    }

    private void StartTitle(string id, ChatSession session, ChatTarget target)
    {
        var entries = session.Entries;
        if (_titles is null
            || _store.Find(id) is not { TitleSource: TitleSource.Placeholder }
            || entries is not [{ Role.Value: "user" } first, .., { Role.Value: "assistant", Status: EntryStatus.Done }]
            || entries.FirstOrDefault(e => e.Role == ChatRole.Assistant && e.Text.Length > 0) is not { } reply)
        {
            return;
        }

        var gate = new TaskCompletionSource();
        if (!_titling.TryAdd(id, gate.Task))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (await _titles.GenerateAsync(target, first.Text.Length > 0 ? first.Text : string.Join(", ", first.Attachments.Select(a => a.File.Name)), reply.Text, _shutdown.Token).ConfigureAwait(false) is { } title)
                {
                    await _store.UpdateAsync(
                        id,
                        s => s.TitleSource == TitleSource.Placeholder ? s with { Title = title, TitleSource = TitleSource.Generated } : s,
                        cancellationToken: _shutdown.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                LogTitleFailed(ex);
            }
            finally
            {
                _titling.TryRemove(id, out _);
                gate.SetResult();
            }
        });
    }

    private static SessionInfo StoreSettings(SessionInfo info, PermissionSettings settings)
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

    private ChatSession NewSession(string id)
    {
        var session = new ChatSession(_clientFactory, _store.GetLog(id), _time, _loggers.CreateLogger<ChatSession>())
        {
            Attachments = new SessionAttachments(_store.AttachmentsDirectory(id)),
        };
        session.Permissions.Sensitive = () => _defaults?.Sensitive(id) ?? SensitivePaths.Default(_store.Paths, id);
        session.Permissions.Sandbox = _sandbox.Status;
        if (_defaults is { } defaults)
        {
            session.Permissions.BypassAllowed = () => defaults.AllowBypass;
            session.Permissions.Rules = () => defaults.Rules;
        }

        var wasRunning = false;
        session.Changed += (_, _) =>
        {
            bool flipped;
            lock (session)
            {
                var running = session.IsRunning;
                flipped = running != wasRunning;
                wasRunning = running;
            }

            if (flipped)
            {
                ActivityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        return session;
    }

    private Task RememberAsync(string id, ChatTarget target, string? firstText, bool replacePlaceholder = false)
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
            touch: true);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not write a title for a session; it keeps its placeholder title.")]
    private partial void LogTitleFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not set up tools for the folder {Folder}; the session has none.")]
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
