using BlazeHarness.Core.Config;
using Microsoft.Extensions.Logging;

namespace BlazeHarness.Core.Mcp;

/// <summary>
/// The global <c>mcp.json</c> as text, so edits keep the file as the user wrote it.
/// </summary>
/// <remarks>
/// The file is read as text instead of a typed model, because it is shared with other tools
/// and the user edits it directly. Saves are atomic, and edits made outside the app are picked up.
/// </remarks>
public sealed partial class McpConfigFile : IDisposable
{
    private const int DebounceMilliseconds = 200;

    private readonly string _path;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly FileSystemWatcher? _watcher;
    private readonly Timer _debounce;
    private Snapshot _current;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpConfigFile"/> class.
    /// </summary>
    /// <param name="path">The path of the file; it does not need to exist.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="watch">Whether to watch the file for edits made outside the app.</param>
    public McpConfigFile(string path, ILogger logger, bool watch = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(logger);
        _path = path;
        _logger = logger;
        _current = Snapshot.Of(ReadText() ?? string.Empty);
        _debounce = new Timer(_ => OnExternalChange());

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
    /// Raised, on any thread, after the file changed outside the app or was saved.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the path of the file.
    /// </summary>
    public string FilePath => _path;

    /// <summary>
    /// Gets the current text of the file, empty when it does not exist.
    /// </summary>
    public string Text => Volatile.Read(ref _current).Text;

    /// <summary>
    /// Gets the result of parsing <see cref="Text"/>.
    /// </summary>
    public McpParseResult Parsed => Volatile.Read(ref _current).Result;

    /// <summary>
    /// Save new text, unless it has errors.
    /// </summary>
    /// <param name="text">The new text of the file.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>The result of parsing the text; the file was saved only when it has no errors.</returns>
    public async Task<McpParseResult> SaveAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var snapshot = Snapshot.Of(text);
        if (snapshot.Result.Errors.Count > 0)
        {
            return snapshot.Result;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AtomicFile.WriteAllTextAsync(_path, text, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _current, snapshot);
        }
        finally
        {
            _lock.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return snapshot.Result;
    }

    /// <summary>
    /// Turn a server on or off for every chat by changing its <c>disabled</c> field.
    /// </summary>
    /// <param name="server">The server name.</param>
    /// <param name="disabled">True to turn the server off.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>True when the file was changed or already had that state; false when the server or the file could not be edited.</returns>
    public async Task<bool> SetDisabledAsync(string server, bool disabled, CancellationToken cancellationToken = default)
    {
        var edited = McpConfigEditor.SetDisabled(Text, server, disabled);
        if (edited is null)
        {
            return false;
        }

        return (await SaveAsync(edited, cancellationToken).ConfigureAwait(false)).Errors.Count == 0;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce.Dispose();
        _lock.Dispose();
    }

    private void Schedule() => _debounce.Change(TimeSpan.FromMilliseconds(DebounceMilliseconds), Timeout.InfiniteTimeSpan);

    private string? ReadText()
    {
        try
        {
            return File.Exists(_path) ? File.ReadAllText(_path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogReadFailed(ex, Path.GetFileName(_path));
            return null;
        }
    }

    private void OnExternalChange()
    {
        _lock.Wait();
        try
        {
            var text = ReadText() ?? string.Empty;
            if (text == _current.Text)
            {
                return;
            }

            Volatile.Write(ref _current, Snapshot.Of(text));
        }
        finally
        {
            _lock.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read {File}.")]
    private partial void LogReadFailed(Exception ex, string file);

    private sealed record Snapshot(string Text, McpParseResult Result)
    {
        public static Snapshot Of(string text) => new(text, McpConfigParser.Parse(text));
    }
}
