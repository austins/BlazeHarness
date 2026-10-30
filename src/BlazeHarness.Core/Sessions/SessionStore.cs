using BlazeHarness.Core.Config;
using BlazeHarness.Core.Folders;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace BlazeHarness.Core.Sessions;

/// <summary>
/// The sessions on disk: one folder each under the sessions directory, with an in-memory index of their metadata.
/// </summary>
/// <remarks>
/// <para>
/// The index is read once at startup, then kept in step with every change made through the store.
/// Metadata files are written atomically, and a delete moves the folder aside before removing it,
/// so a crash cannot leave a half-deleted session that still shows up.
/// </para>
/// <para>
/// Members are safe to call from any thread. <see cref="Changed"/> is raised on the thread that made the change.
/// </para>
/// </remarks>
public sealed partial class SessionStore : IDisposable
{
    private const string MetadataFile = "session.json";
    private const string LogFile = "messages.jsonl";
    private const string AttachmentsFolder = "attachments";
    private const string TrashPrefix = ".trash-";

    private readonly AppPaths _paths;
    private readonly ILoggerFactory _loggers;
    private readonly ILogger<SessionStore> _logger;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, SessionInfo> _index = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SessionLog> _logs = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionStore"/> class and loads the index.
    /// </summary>
    /// <param name="paths">Resolves the sessions directory.</param>
    /// <param name="loggers">Creates loggers for the store and its message logs.</param>
    /// <param name="time">The clock for timestamps.</param>
    public SessionStore(AppPaths paths, ILoggerFactory loggers, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(loggers);
        ArgumentNullException.ThrowIfNull(time);
        _paths = paths;
        _loggers = loggers;
        _logger = loggers.CreateLogger<SessionStore>();
        _time = time;
        LoadIndex();
    }

    /// <summary>
    /// Raised after a session is created, changed or deleted.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Get sessions, newest activity first.
    /// </summary>
    /// <param name="titleFilter">Keep only sessions whose title contains this text, ignoring case; null or blank keeps all.</param>
    /// <returns>The matching sessions.</returns>
    public IReadOnlyList<SessionInfo> List(string? titleFilter = null)
    {
        var all = _index.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(titleFilter))
        {
            var filter = titleFilter.Trim();
            all = all.Where(s => s.Title.Contains(filter, StringComparison.CurrentCultureIgnoreCase));
        }

        return [.. all.OrderByDescending(s => s.UpdatedAt).ThenBy(s => s.Id, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Find a session.
    /// </summary>
    /// <param name="id">The session id.</param>
    /// <returns>The session, or null when there is none with that id.</returns>
    public SessionInfo? Find(string id)
    {
        return _index.GetValueOrDefault(id);
    }

    /// <summary>
    /// Gets the app's folders.
    /// </summary>
    public AppPaths Paths => _paths;

    /// <summary>
    /// Get the folder a session's confined shell commands keep their home and temporary files in.
    /// </summary>
    /// <param name="id">The session id.</param>
    /// <returns>The folder path, which may not exist yet.</returns>
    public string ShellDirectory(string id) => Path.Combine(_paths.SessionDirectory(id), "shell");

    /// <summary>
    /// Get the folder where a chat without a folder works: the model's working directory and the only place it may write there.
    /// </summary>
    /// <param name="id">The session id.</param>
    /// <returns>The folder path, which may not exist yet.</returns>
    public string OutputsDirectory(string id) => Path.Combine(_paths.SessionDirectory(id), "outputs");

    /// <summary>
    /// Get the folder a session keeps its attached files in.
    /// </summary>
    /// <param name="id">The session id.</param>
    /// <returns>The full path.</returns>
    public string AttachmentsDirectory(string id) => Path.Combine(_paths.SessionDirectory(id), AttachmentsFolder);

    /// <summary>
    /// Get the message log of a session.
    /// </summary>
    /// <remarks>
    /// The same instance is returned for a session every time, so appends from different callers stay serialized.
    /// </remarks>
    /// <param name="id">The session id.</param>
    /// <returns>The log, which is created on disk on its first append.</returns>
    public SessionLog GetLog(string id)
    {
        return _logs.GetOrAdd(id, key => new SessionLog(Path.Combine(_paths.SessionDirectory(key), LogFile), _loggers.CreateLogger<SessionLog>()));
    }

    /// <summary>
    /// Create a new, empty session.
    /// </summary>
    /// <param name="folderPath">The folder the session works in, or null for none.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>The new session.</returns>
    public async Task<SessionInfo> CreateAsync(string? folderPath = null, CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        var info = new SessionInfo
        {
            Id = Guid.CreateVersion7().ToString("N"),
            CreatedAt = now,
            UpdatedAt = now,
            FolderPath = folderPath,
        };

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await WriteAsync(info, cancellationToken);
            _index[info.Id] = info;
        }
        finally
        {
            _lock.Release();
        }

        RaiseChanged();
        return info;
    }

    /// <summary>
    /// Change a session's metadata.
    /// </summary>
    /// <param name="id">The session id.</param>
    /// <param name="mutate">Returns the new metadata from the current one. The id cannot be changed.</param>
    /// <param name="touch">Whether to set <see cref="SessionInfo.UpdatedAt"/> to now, so the session moves to the top of the list.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>The updated session, or null when it no longer exists.</returns>
    public async Task<SessionInfo?> UpdateAsync(string id, Func<SessionInfo, SessionInfo> mutate, bool touch = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        SessionInfo updated;
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!_index.TryGetValue(id, out var current))
            {
                return null;
            }

            updated = mutate(current) with { Id = id };
            if (touch)
            {
                updated = updated with { UpdatedAt = _time.GetUtcNow() };
            }

            await WriteAsync(updated, cancellationToken);
            _index[id] = updated;
        }
        finally
        {
            _lock.Release();
        }

        RaiseChanged();
        return updated;
    }

    /// <summary>
    /// Move every session that works in one folder to another folder, or detach them from any folder.
    /// </summary>
    /// <param name="oldPath">The folder the sessions currently work in.</param>
    /// <param name="newPath">The folder to move them to, or null to detach them.</param>
    /// <param name="cancellationToken">A token to cancel the writes.</param>
    /// <returns>The number of sessions that were changed.</returns>
    public async Task<int> RepointFolderAsync(string oldPath, string? newPath, CancellationToken cancellationToken = default)
    {
        var moved = 0;
        var ids = _index.Values.Where(s => s.FolderPath is { } path && FolderPaths.Comparer.Equals(path, oldPath)).Select(s => s.Id).ToList();
        foreach (var id in ids)
        {
            var updated = await UpdateAsync(id, s => s with { FolderPath = newPath }, cancellationToken: cancellationToken);
            moved += updated is null ? 0 : 1;
        }

        return moved;
    }

    /// <summary>
    /// Rename a session to a title the user chose, which generated titles never replace.
    /// </summary>
    /// <param name="id">The session id.</param>
    /// <param name="title">The new title; surrounding white space is trimmed.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>The updated session, or null when it no longer exists.</returns>
    public Task<SessionInfo?> RenameAsync(string id, string title, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(title);
        var trimmed = title.Trim();
        return UpdateAsync(id, s => s with { Title = trimmed, TitleSource = TitleSource.User }, touch: false, cancellationToken);
    }

    /// <summary>
    /// Delete a session and everything stored for it.
    /// </summary>
    /// <remarks>
    /// The folder is renamed first and removed after, so a crash in between leaves no session that looks half deleted.
    /// Files the model created in the user's own folders are never touched.
    /// </remarks>
    /// <param name="id">The session id.</param>
    /// <param name="cancellationToken">A token to cancel the wait for the store.</param>
    /// <returns>A task that completes when the session is gone.</returns>
    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var directory = _paths.SessionDirectory(id);

        string? trash = null;
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_logs.TryRemove(id, out var log))
            {
                log.Dispose();
            }

            // Move before touching the index, so a failed move leaves the session listed.
            if (Directory.Exists(directory))
            {
                trash = Path.Combine(_paths.SessionsDirectory, TrashPrefix + Guid.NewGuid().ToString("N")[..8]);
                Directory.Move(directory, trash);
            }

            _index.TryRemove(id, out _);
        }
        finally
        {
            _lock.Release();
        }

        if (trash is not null)
        {
            RemoveTrash(trash);
        }

        RaiseChanged();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var log in _logs.Values)
        {
            log.Dispose();
        }

        _lock.Dispose();
    }

    private void LoadIndex()
    {
        var root = _paths.SessionsDirectory;
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(directory);
            if (name.StartsWith(TrashPrefix, StringComparison.Ordinal))
            {
                RemoveTrash(directory);
                continue;
            }

            try
            {
                var info = JsonSerializer.Deserialize<SessionInfo>(File.ReadAllText(Path.Combine(directory, MetadataFile)), JsonDefaults.Options);
                if (info is not null)
                {
                    _index[name] = info with { Id = name };
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                LogUnreadableSession(ex, name);
            }
        }
    }

    private Task WriteAsync(SessionInfo info, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_paths.SessionDirectory(info.Id), MetadataFile);
        return AtomicFile.WriteAllTextAsync(path, JsonSerializer.Serialize(info, JsonDefaults.Options), cancellationToken);
    }

    private void RemoveTrash(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogTrashNotRemoved(ex, Path.GetFileName(directory));
        }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping session {Session}: its metadata could not be read.")]
    private partial void LogUnreadableSession(Exception ex, string session);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not remove the deleted session folder {Folder}; it will be retried at the next start.")]
    private partial void LogTrashNotRemoved(Exception ex, string folder);
}
