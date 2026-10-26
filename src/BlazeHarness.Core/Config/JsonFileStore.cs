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
    private const int DebounceMilliseconds = 200;

    private readonly string _path;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly FileSystemWatcher? _watcher;
    private readonly Timer _debounce;
    private T _current;
    private string? _lastWrittenJson;

    public JsonFileStore(string path, ILogger logger, bool watch = true)
    {
        _path = path;
        _logger = logger;
        _current = ReadFromDisk() ?? new T();
        _debounce = new Timer(_ => OnExternalChange());

        if (watch)
        {
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            _watcher = new FileSystemWatcher(directory, Path.GetFileName(_path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, _) => _debounce.Change(TimeSpan.FromMilliseconds(DebounceMilliseconds), Timeout.InfiniteTimeSpan);
            _watcher.Created += (_, _) => _debounce.Change(TimeSpan.FromMilliseconds(DebounceMilliseconds), Timeout.InfiniteTimeSpan);
            _watcher.Renamed += (_, _) => _debounce.Change(TimeSpan.FromMilliseconds(DebounceMilliseconds), Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Raised after an external edit (not our own writes) changed the value.</summary>
    public event EventHandler<ValueEventArgs<T>>? ExternalChange;

    public T Current => Volatile.Read(ref _current);

    public async Task UpdateAsync(Func<T, T> mutate, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var updated = mutate(_current);
            var json = JsonSerializer.Serialize(updated, JsonDefaults.Options);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            var temp = _path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            await File.WriteAllTextAsync(temp, json, cancellationToken);
            File.Move(temp, _path, overwrite: true);

            _lastWrittenJson = json;
            Volatile.Write(ref _current, updated);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce.Dispose();
        _lock.Dispose();
    }

    private T? ReadFromDisk()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<T>(File.ReadAllText(_path), JsonDefaults.Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
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
            string? text;
            try
            {
                text = File.Exists(_path) ? File.ReadAllText(_path) : null;
            }
            catch (IOException)
            {
                _debounce.Change(TimeSpan.FromMilliseconds(DebounceMilliseconds), Timeout.InfiniteTimeSpan);
                return;
            }

            if (text is null || text == _lastWrittenJson)
            {
                return;
            }

            T? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<T>(text, JsonDefaults.Options);
            }
            catch (JsonException ex)
            {
                LogInvalidExternalEdit(ex, Path.GetFileName(_path));
                return;
            }

            if (parsed is null)
            {
                return;
            }

            _lastWrittenJson = text;
            Volatile.Write(ref _current, parsed);
            ExternalChange?.Invoke(this, new ValueEventArgs<T>(parsed));
        }
        finally
        {
            _lock.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read {File}; keeping the previous value.")]
    private partial void LogReadFailed(Exception ex, string file);

    [LoggerMessage(Level = LogLevel.Warning, Message = "External edit of {File} is not valid JSON; ignoring it.")]
    private partial void LogInvalidExternalEdit(Exception ex, string file);
}

public sealed class ValueEventArgs<T>(T value) : EventArgs
{
    public T Value { get; } = value;
}
