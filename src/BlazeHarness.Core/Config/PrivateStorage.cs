using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace BlazeHarness.Core.Config;

/// <summary>
/// Creates app-owned files with access limited to the current account.
/// </summary>
internal static class PrivateStorage
{
    private const UnixFileMode FileModeBits = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>
    /// Open a private file for writing, restricting an append target before any content is written.
    /// </summary>
    /// <param name="path">The app-owned file.</param>
    /// <param name="mode">How to create or open the file.</param>
    /// <param name="share">The access other handles may request.</param>
    /// <param name="bufferSize">The stream buffer size.</param>
    /// <param name="options">The stream's synchronous or asynchronous options.</param>
    /// <returns>A writable stream owned by the caller.</returns>
    public static FileStream OpenWrite(
        string path,
        FileMode mode,
        FileShare share = FileShare.None,
        int bufferSize = 4096,
        FileOptions options = FileOptions.Asynchronous)
    {
        if (new FileInfo(path).LinkTarget is not null)
        {
            throw new IOException("App-private files cannot be symbolic links.");
        }

        if (OperatingSystem.IsWindows())
        {
            return OpenWriteWindows(path, mode, share, bufferSize, options);
        }

        var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = mode,
            Access = FileAccess.Write,
            Share = share,
            BufferSize = bufferSize,
            Options = options,
            UnixCreateMode = FileModeBits,
        });
        try
        {
            File.SetUnixFileMode(stream.SafeFileHandle, FileModeBits);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    [SupportedOSPlatform("windows")]
    private static FileStream OpenWriteWindows(string path, FileMode mode, FileShare share, int bufferSize, FileOptions options)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        using var identity = WindowsIdentity.GetCurrent();
        security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl, AccessControlType.Allow));
        // Updating the DACL also needs ReadPermissions, which the Append overload rejects.
        var stream = new FileInfo(path).Create(
            mode == FileMode.Append ? FileMode.OpenOrCreate : mode,
            FileSystemRights.Write | FileSystemRights.ReadPermissions | FileSystemRights.ChangePermissions,
            share,
            bufferSize,
            options,
            security);
        try
        {
            stream.SetAccessControl(security);
            if (mode == FileMode.Append)
            {
                // The ACL-aware create overload opens append targets at position zero.
                stream.Seek(0, SeekOrigin.End);
            }

            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
}
