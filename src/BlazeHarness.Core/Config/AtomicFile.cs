using System.Text;

namespace BlazeHarness.Core.Config;

/// <summary>
/// Writes a file so a crash never leaves it half written: the content goes to a temp file first, then replaces the target.
/// </summary>
internal static class AtomicFile
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Write bytes to a file atomically, keeping the Unix permissions of the file it replaces.
    /// </summary>
    /// <param name="path">The full path of the file to create or replace.</param>
    /// <param name="bytes">The content.</param>
    public static void Write(string path, byte[] bytes)
    {
        var folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(folder);
        var temp = Path.Combine(folder, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = CreateStagingFile(temp, path))
            {
                stream.Write(bytes);

                // Without this a power cut after the rename can leave an empty file.
                stream.Flush(flushToDisk: true);
            }

            if (!OperatingSystem.IsWindows() && File.Exists(path))
            {
                File.SetUnixFileMode(temp, File.GetUnixFileMode(path));
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    /// <summary>
    /// Write text to a file atomically, with the temp file readable only by the current user before any content is written.
    /// </summary>
    /// <param name="path">The target file.</param>
    /// <param name="content">The text to write.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>A task that completes once the target holds the new content.</returns>
    public static async Task WriteAllOwnerOnlyTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            await using (var stream = PrivateStorage.OpenWrite(temp, FileMode.CreateNew))
            {
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

    internal static FileStream CreateStagingFile(string path, string target)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows() && File.Exists(target))
        {
            options.UnixCreateMode = File.GetUnixFileMode(target);
        }

        return new FileStream(path, options);
    }
}
