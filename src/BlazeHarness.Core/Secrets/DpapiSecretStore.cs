using BlazeHarness.Core.Config;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BlazeHarness.Core.Secrets;

/// <summary>
/// Windows: values encrypted with DPAPI for the current user and kept in a file.
/// </summary>
/// <param name="path">The full path of the file holding the encrypted values.</param>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore(string path) : ISecretStore
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <inheritdoc />
    public string Name => "Windows DPAPI";

    /// <inheritdoc />
    public bool IsSecure => true;

    /// <inheritdoc />
    public void Dispose()
    {
        _lock.Dispose();
    }

    /// <inheritdoc />
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var all = await ReadAsync(cancellationToken);
            if (!all.TryGetValue(key, out var protectedValue))
            {
                return null;
            }

            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
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
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
            all[key] = Convert.ToBase64String(bytes);
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

    private async Task<Dictionary<string, string>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonDefaults.Options) ?? [];
    }

    private async Task WriteAsync(Dictionary<string, string> all, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(all, JsonDefaults.Options), cancellationToken);
        File.Move(temp, path, overwrite: true);
    }
}
