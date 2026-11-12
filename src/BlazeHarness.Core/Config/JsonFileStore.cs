using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BlazeHarness.Core.Config;

/// <summary>
/// One JSON file read and written through a single object: atomic writes (temp file, then rename),
/// a lock against read-modify-write races, and a watcher that picks up external edits.
/// </summary>
/// <typeparam name="T">The document type.</typeparam>
public sealed partial class JsonFileStore<T> : IDisposable
    where T : class, new()
{
    private readonly string _path;
    private readonly ILogger _logger;
    private readonly WatchedFile _file;
    private T _current;
    private string? _lastWrittenJson;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonFileStore{T}"/> class and loads the file.
    /// </summary>
    /// <remarks>
    /// A missing or invalid file yields a default <typeparamref name="T"/>; the invalid file is left untouched until the next update.
    /// </remarks>
    /// <param name="path">The full path of the JSON file.</param>
    /// <param name="logger">The logger for read failures.</param>
    /// <param name="time">Times the delay that merges bursts of file events.</param>
    /// <param name="watch">Whether to watch the file and raise <see cref="ExternalChange"/> on outside edits.</param>
    public JsonFileStore(string path, ILogger logger, TimeProvider time, bool watch = true)
    {
        ArgumentNullException.ThrowIfNull(time);
        _path = path;
        _logger = logger;
        _file = new WatchedFile(path, logger, time, watch, watchDelete: false, ReloadUnderLock);
        _current = ParseInitial(_file.ReadInitialText()) ?? new T();
    }

    /// <summary>
    /// Raised after an external edit (not our own writes) changed the value.
    /// </summary>
    /// <remarks>
    /// It is raised on a thread-pool thread; UI code must marshal to its own context.
    /// </remarks>
    public event EventHandler<ValueEventArgs<T>>? ExternalChange;

    /// <summary>
    /// Gets the latest value. Treat it as immutable and change it through <see cref="UpdateAsync"/>.
    /// </summary>
    public T Current => Volatile.Read(ref _current);

    /// <summary>
    /// Apply a change and write it to disk atomically.
    /// </summary>
    /// <remarks>
    /// Updates are serialized, so concurrent callers each see the result of the previous one.
    /// </remarks>
    /// <param name="mutate">Returns the new value from the current one. It should not mutate its argument.</param>
    /// <param name="cancellationToken">A token to cancel the wait for the lock or the write.</param>
    /// <returns>A task that completes once the new value is on disk and visible through <see cref="Current"/>.</returns>
    public async Task UpdateAsync(Func<T, T> mutate, CancellationToken cancellationToken = default)
    {
        await _file.WaitAsync(cancellationToken);
        try
        {
            var updated = mutate(_current);
            var json = JsonSerializer.Serialize(updated, JsonDefaults.Options);
            await AtomicFile.WriteAllOwnerOnlyTextAsync(_path, json, cancellationToken);

            _lastWrittenJson = json;
            Volatile.Write(ref _current, updated);
        }
        finally
        {
            _file.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _file.Dispose();

    /// <summary>
    /// Read the file again after an edit made outside the app, and raise <see cref="ExternalChange"/> when it holds a new valid value.
    /// </summary>
    internal void OnExternalChange() => _file.OnExternalChange();

    private T? ParseInitial(string? text)
    {
        if (text is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(text, JsonDefaults.Options);
        }
        catch (JsonException ex)
        {
            LogInvalidInitialJson(ex, Path.GetFileName(_path));
            return null;
        }
    }

    private Action? ReloadUnderLock()
    {
        if (!_file.TryReadCurrentText(out var text) || text is null || text == _lastWrittenJson)
        {
            return null;
        }

        T? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<T>(text, JsonDefaults.Options);
        }
        catch (JsonException ex)
        {
            LogInvalidExternalEdit(ex, Path.GetFileName(_path));
            return null;
        }

        if (parsed is null)
        {
            return null;
        }

        _lastWrittenJson = text;
        Volatile.Write(ref _current, parsed);
        return () => ExternalChange?.Invoke(this, new ValueEventArgs<T>(parsed));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read {File}; keeping the previous value.")]
    private partial void LogInvalidInitialJson(Exception ex, string file);

    [LoggerMessage(Level = LogLevel.Warning, Message = "External edit of {File} is not valid JSON; ignoring it.")]
    private partial void LogInvalidExternalEdit(Exception ex, string file);
}
