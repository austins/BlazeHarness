using BlazeHarness.Core.Agent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BlazeHarness.Core.Sessions;

/// <summary>
/// The app-wide owner of conversations: it keeps each opened <see cref="ChatSession"/> alive independent of any page,
/// so a reply carries on when the tab that started it is closed and any tab can pick it up again.
/// </summary>
/// <remarks>
/// Sessions are restored from their log on first use and then kept in memory until they are deleted.
/// </remarks>
public sealed class SessionService : IDisposable
{
    private const int PlaceholderLength = 48;

    private readonly SessionStore _store;
    private readonly Func<ChatTarget, CancellationToken, Task<IChatClient>> _clientFactory;
    private readonly ILoggerFactory _loggers;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, ChatSession> _live = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _opening = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionService"/> class.
    /// </summary>
    /// <param name="store">The saved sessions.</param>
    /// <param name="clientFactory">Creates the chat client for a target.</param>
    /// <param name="loggers">Creates loggers for the sessions.</param>
    /// <param name="time">The clock for timestamps; defaults to the system clock.</param>
    public SessionService(
        SessionStore store,
        Func<ChatTarget, CancellationToken, Task<IChatClient>> clientFactory,
        ILoggerFactory loggers,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(loggers);
        _store = store;
        _clientFactory = clientFactory;
        _loggers = loggers;
        _time = time ?? TimeProvider.System;
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
        _live[info.Id] = NewSession(info.Id);
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
        await session.SendAsync(text, target).ConfigureAwait(false);
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
        await session.RetryAsync(target).ConfigureAwait(false);
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
        foreach (var session in _live.Values)
        {
            session.Dispose();
        }

        _opening.Dispose();
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

    private Task RememberAsync(string id, ChatTarget target, string? firstText)
    {
        return _store.UpdateAsync(
            id,
            s => s with
            {
                ProviderId = target.Provider.Id,
                ModelId = target.Model.Id,
                Level = target.Level,
                Title = s.Title.Length == 0 && firstText is not null ? Placeholder(firstText) : s.Title,
            },
            touch: true);
    }

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
