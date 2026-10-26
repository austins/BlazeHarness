using BlazeHarness.Core.Config;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace BlazeHarness.Core.Secrets;

/// <summary>
/// Plaintext JSON file readable only by the current user (mode 0600, or an owner-only ACL on Windows).
/// </summary>
/// <remarks>
/// A fallback for machines without a keyring; the UI flags it as insecure.
/// </remarks>
/// <param name="path">The full path of the secrets file.</param>
public sealed class FileSecretStore(string path) : ISecretStore
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <inheritdoc />
    public string Name => "Plaintext file (owner-only)";

    /// <inheritdoc />
    public bool IsSecure => false;

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
            return all.GetValueOrDefault(key);
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
            all[key] = value;
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
        var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";

        // Create with owner-only permissions before any secret is written, then replace atomically.
        if (OperatingSystem.IsWindows())
        {
            await File.WriteAllTextAsync(temp, string.Empty, cancellationToken);
            RestrictToOwnerWindows(temp);
        }
        else
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
            };
            await using var created = new FileStream(temp, options);
        }

        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(all, JsonDefaults.Options), cancellationToken);
        File.Move(temp, path, overwrite: true);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void RestrictToOwnerWindows(string file)
    {
        var info = new FileInfo(file);
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var user = WindowsIdentity.GetCurrent().User!;
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        info.SetAccessControl(security);
    }
}
