using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace BlazeHarness.Core.Config;

/// <summary>
/// Writes a file so a crash never leaves it half written: the content goes to a temp file first, then replaces the target.
/// </summary>
internal static class AtomicFile
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Write text to a file atomically, creating its folder when needed.
    /// </summary>
    /// <param name="path">The target file.</param>
    /// <param name="content">The text to write.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>A task that completes once the target holds the new content.</returns>
    public static Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        return WriteAsync(path, content, ownerOnly: false, cancellationToken);
    }

    /// <summary>
    /// Write text to a file atomically, with the temp file readable only by the current user before any content is written.
    /// </summary>
    /// <param name="path">The target file.</param>
    /// <param name="content">The text to write.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>A task that completes once the target holds the new content.</returns>
    public static Task WriteAllOwnerOnlyTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        return WriteAsync(path, content, ownerOnly: true, cancellationToken);
    }

    private static async Task WriteAsync(string path, string content, bool ownerOnly, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 4096,
                Options = FileOptions.Asynchronous,
            };
            if (ownerOnly && !OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            var stream = new FileStream(temp, options);
            await using (stream.ConfigureAwait(false))
            {
                if (ownerOnly && OperatingSystem.IsWindows())
                {
                    RestrictToOwnerWindows(stream);
                }

                await stream.WriteAsync(Utf8WithoutBom.GetBytes(content), cancellationToken);

                // Without this a power cut after the rename can leave an empty file.
#pragma warning disable CA1849 // FlushAsync can't ask the operating system to write to the disk.
                stream.Flush(flushToDisk: true);
#pragma warning restore CA1849
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void RestrictToOwnerWindows(FileStream stream)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var user = WindowsIdentity.GetCurrent().User!;
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        stream.SetAccessControl(security);
    }
}
