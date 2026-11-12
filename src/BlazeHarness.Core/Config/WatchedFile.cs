using Microsoft.Extensions.Logging;

namespace BlazeHarness.Core.Config;

/// <summary>
/// Serializes reads and writes of a watched text file, debounces bursts of watcher events, and guards change handlers.
/// </summary>
internal sealed partial class WatchedFile : IDisposable
{
    private const int DebounceMilliseconds = 200;

    private readonly string _path;
    private readonly ILogger _logger;
    private readonly Func<Action?> _reloadUnderLock;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly FileSystemWatcher? _watcher;
    private readonly ITimer _debounce;

    /// <summary>
    /// 1 while a reload is waiting for the lock, so contended changes share that one reload.
    /// </summary>
    private int _reloadPending;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchedFile"/> class.
    /// </summary>
    /// <param name="path">The full or relative path of the file.</param>
    /// <param name="logger">The logger for read and handler failures.</param>
    /// <param name="time">Times the debounce delay.</param>
    /// <param name="watch">Whether to watch the file on disk.</param>
    /// <param name="watchDelete">Whether file deletion also triggers a reload.</param>
    /// <param name="reloadUnderLock">Runs under the file lock and returns an optional post-lock notification when the value changed.</param>
    public WatchedFile(string path, ILogger logger, TimeProvider time, bool watch, bool watchDelete, Func<Action?> reloadUnderLock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(reloadUnderLock);
        _path = path;
        _logger = logger;
        _reloadUnderLock = reloadUnderLock;
        _debounce = time.CreateTimer(_ => OnExternalChange(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        if (watch)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(_path))!;
            Directory.CreateDirectory(directory);
            _watcher = new FileSystemWatcher(directory, Path.GetFileName(_path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, _) => Schedule();
            _watcher.Created += (_, _) => Schedule();
            _watcher.Renamed += (_, _) => Schedule();
            if (watchDelete)
            {
                _watcher.Deleted += (_, _) => Schedule();
            }
        }
    }

    /// <summary>
    /// Acquire the file lock asynchronously.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel waiting for the lock.</param>
    /// <returns>A task that completes when the lock is acquired.</returns>
    public Task WaitAsync(CancellationToken cancellationToken = default) => _lock.WaitAsync(cancellationToken);

    /// <summary>
    /// Release the file lock.
    /// </summary>
    public void Release() => _lock.Release();

    /// <summary>
    /// Read the file at startup, returning null when it does not exist or cannot be read.
    /// </summary>
    /// <returns>The file's text, or null.</returns>
    public string? ReadInitialText()
    {
        try
        {
            return File.Exists(_path) ? File.ReadAllText(_path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogReadFailed(_logger, ex, Path.GetFileName(_path));
            return null;
        }
    }

    /// <summary>
    /// Read the file during an external reload, scheduling a retry when another process still holds it.
    /// </summary>
    /// <param name="text">The file's text when present, or null when missing.</param>
    /// <returns>True when the file state was read; false when reading failed and a retry was scheduled.</returns>
    public bool TryReadCurrentText(out string? text)
    {
        try
        {
            text = File.Exists(_path) ? File.ReadAllText(_path) : null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Schedule();
            text = null;
            return false;
        }
    }

    /// <summary>
    /// Schedule a debounced reload.
    /// </summary>
    public void Schedule()
    {
        try
        {
            _debounce.Change(TimeSpan.FromMilliseconds(DebounceMilliseconds), Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // A watcher event can still arrive just after disposal.
        }
    }

    /// <summary>
    /// Reload the file without blocking a thread-pool thread when a save is in progress.
    /// </summary>
    public void OnExternalChange()
    {
        Action? notify;
        try
        {
            if (!_lock.Wait(0))
            {
                if (Interlocked.Exchange(ref _reloadPending, 1) == 0)
                {
                    _ = ReloadWhenFreeAsync();
                }

                return;
            }

            try
            {
                notify = _reloadUnderLock();
            }
            finally
            {
                _lock.Release();
            }
        }
        catch (ObjectDisposedException)
        {
            // A callback that was already queued when the store was disposed has nothing left to do, and must not fault a thread-pool thread.
            return;
        }

        if (notify is not null)
        {
            SafeInvoke(notify);
        }
    }

    /// <summary>
    /// Run an event notification outside the lock so a throwing subscriber cannot bring down the process.
    /// </summary>
    /// <param name="notify">The notification to run.</param>
    public void SafeInvoke(Action notify)
    {
        try
        {
            notify();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogHandlerFailed(_logger, ex, Path.GetFileName(_path));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce.Dispose();
        _lock.Dispose();
    }

    private async Task ReloadWhenFreeAsync()
    {
        Action? notify;
        try
        {
            await _lock.WaitAsync();
            try
            {
                // Cleared before reading, so a change after this point starts its own reload instead of being missed.
                Volatile.Write(ref _reloadPending, 0);
                notify = _reloadUnderLock();
            }
            finally
            {
                _lock.Release();
            }
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (notify is not null)
        {
            SafeInvoke(notify);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read {File}; keeping the previous value.")]
    private static partial void LogReadFailed(ILogger logger, Exception ex, string file);

    [LoggerMessage(Level = LogLevel.Error, Message = "A change handler for {File} threw.")]
    private static partial void LogHandlerFailed(ILogger logger, Exception ex, string file);
}
