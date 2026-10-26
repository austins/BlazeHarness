using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Folders;
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
    public SessionService(
        SessionStore store,
        Func<ChatTarget, CancellationToken, Task<IChatClient>> clientFactory,
        ILoggerFactory loggers,
        TimeProvider? time = null,
        ITitleGenerator? titles = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(loggers);
        _store = store;
        _clientFactory = clientFactory;
        _loggers = loggers;
        _time = time ?? TimeProvider.System;
        _titles = titles;
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
        var session = NewSession(info.Id);
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
    /// Send a message in a session, and remember the target it was sent with.
    /// </summary>
    /// <param name="id">The session id.</param>
    /// <param name="text">The message text.</param>
    /// <param name="target">The provider, model and level to use.</param>
    /// <returns>A task that completes when the reply has ended; it does nothing when the session is unknown, busy, or the text is blank.</returns>
    public async Task SendAsync(string id, string text, ChatTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (await OpenAsync(id).ConfigureAwait(false) is not { IsRunning: false } session || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        await RememberAsync(id, target, text).ConfigureAwait(false);
        await ApplyFolderAsync(id, session).ConfigureAwait(false);
        await session.SendAsync(text, target).ConfigureAwait(false);
        StartTitle(id, session, target);
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

        ApplyTools(session, folderPath);
    }

    private void ApplyTools(ChatSession session, string? folderPath)
    {
        if (folderPath is null || !Directory.Exists(folderPath))
        {
            session.Tools = null;
            session.BasePrompt = null;
            return;
        }

        try
        {
            var tools = BuiltInTools.Create(folderPath);
            if (session.Tools?.Context.Root != tools.Context.Root)
            {
                session.Tools = tools;
                session.BasePrompt = SystemPrompt.Build(RuntimeInformation.OSDescription, shell: null, tools.Context.Root);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LogToolsUnavailable(ex, folderPath);
            session.Tools = null;
            session.BasePrompt = null;
        }
    }

    private void StartTitle(string id, ChatSession session, ChatTarget target)
    {
        if (_titles is null
            || _store.Find(id) is not { TitleSource: TitleSource.Placeholder }
            || session.Entries is not [{ Role.Value: "user" } first, { Role.Value: "assistant", Status: EntryStatus.Done, Text.Length: > 0 } reply, ..])
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
                if (await _titles.GenerateAsync(target, first.Text, reply.Text, _shutdown.Token).ConfigureAwait(false) is { } title)
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

    private ChatSession NewSession(string id)
    {
        var session = new ChatSession(_clientFactory, _store.GetLog(id), _time, _loggers.CreateLogger<ChatSession>());
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
