using Microsoft.Extensions.Logging;

namespace BlazeHarness.Core.Config;

/// <summary>
/// The user's custom instructions, kept as text in <c>instructions.md</c> so they can be edited like any other Markdown file.
/// </summary>
/// <remarks>
/// Saves are atomic, and edits made outside the app are picked up. A file that cannot be read keeps the last good text.
/// </remarks>
public sealed partial class CustomInstructionsFile : IDisposable
{
    private const int DebounceMilliseconds = 200;

    private readonly string _path;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly FileSystemWatcher? _watcher;
    private readonly ITimer _debounce;
    private string _text;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomInstructionsFile"/> class and reads the file.
    /// </summary>
    /// <param name="path">The path of the file; it does not need to exist.</param>
    /// <param name="logger">The logger for read failures.</param>
    /// <param name="time">Times the delay that merges bursts of file events.</param>
    /// <param name="watch">Whether to watch the file for edits made outside the app.</param>
    public CustomInstructionsFile(string path, ILogger logger, TimeProvider time, bool watch = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(time);
        _path = path;
        _logger = logger;
        _text = ReadText() ?? string.Empty;
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
            _watcher.Deleted += (_, _) => Schedule();
            _watcher.Renamed += (_, _) => Schedule();
        }
    }

    /// <summary>
    /// Raised, on any thread, after the text changed because the file was edited outside the app.
    /// </summary>
    public event EventHandler? ExternalChange;

    /// <summary>
    /// Gets the current text, empty when the file does not exist.
    /// </summary>
    public string Text => Volatile.Read(ref _text);

    /// <summary>
    /// Save new text.
    /// </summary>
    /// <param name="text">The new text of the file.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>A task that completes once the text is on disk and visible through <see cref="Text"/>.</returns>
    public async Task SaveAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await AtomicFile.WriteAllOwnerOnlyTextAsync(_path, text, cancellationToken);
            Volatile.Write(ref _text, text);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce.Dispose();
        _lock.Dispose();
    }

    /// <summary>
    /// Read the file again after an edit made outside the app, and raise <see cref="ExternalChange"/> when its text differs.
    /// </summary>
    internal void OnExternalChange()
    {
        bool changed;
        try
        {
            changed = Reload();
        }
        catch (ObjectDisposedException)
        {
            // A callback that was already queued when the file was disposed has nothing left to do, and must not fault a thread-pool thread.
            return;
        }

        if (changed)
        {
            ExternalChange?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Schedule()
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

    private string? ReadText()
    {
        try
        {
            return File.Exists(_path) ? File.ReadAllText(_path) : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogReadFailed(ex, Path.GetFileName(_path));
            return null;
        }
    }

    private bool Reload()
    {
        _lock.Wait();
        try
        {
            string text;
            try
            {
                text = File.Exists(_path) ? File.ReadAllText(_path) : string.Empty;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Often the writer still holds the file. Keep the text that was loaded instead of treating it as empty, and try again shortly.
                Schedule();
                return false;
            }

            if (text == _text)
            {
                return false;
            }

            Volatile.Write(ref _text, text);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read {File}.")]
    private partial void LogReadFailed(Exception ex, string file);
}
