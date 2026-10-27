using BlazeHarness.Core.Config;
using System.Text.Json;

namespace BlazeHarness.Core.Secrets;

/// <summary>
/// A secret store that keeps a JSON dictionary in one file, passing each value through protect and unprotect delegates.
/// </summary>
/// <remarks>
/// A file that is not valid JSON is moved aside to <c>.corrupt</c> and treated as empty, so one bad write
/// does not leave every provider unusable.
/// </remarks>
/// <param name="path">The full path of the secrets file.</param>
/// <param name="protect">Turns a plain value into the text stored in the file.</param>
/// <param name="unprotect">Turns a stored value back into the plain value.</param>
/// <param name="ownerOnly">True to create the file readable only by the current user.</param>
internal abstract class FileBackedSecretStore(
    string path,
    Func<string, string> protect,
    Func<string, string> unprotect,
    bool ownerOnly) : ISecretStore
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract bool IsSecure { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var all = await ReadAsync(cancellationToken);
            return all.TryGetValue(key, out var stored) ? unprotect(stored) : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var all = await ReadAsync(cancellationToken);
            all[key] = protect(value);
            await WriteAsync(all, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var all = await ReadAsync(cancellationToken);
            if (all.Remove(key))
            {
                await WriteAsync(all, cancellationToken);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Release the resources held by the store.
    /// </summary>
    /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lock.Dispose();
        }
    }

    private async Task<Dictionary<string, string>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonDefaults.Options) ?? [];
        }
        catch (JsonException)
        {
            File.Move(path, path + ".corrupt", overwrite: true);
            return [];
        }
    }

    private Task WriteAsync(Dictionary<string, string> all, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(all, JsonDefaults.Options);
        return ownerOnly
            ? AtomicFile.WriteAllOwnerOnlyTextAsync(path, json, cancellationToken)
            : AtomicFile.WriteAllTextAsync(path, json, cancellationToken);
    }
}
