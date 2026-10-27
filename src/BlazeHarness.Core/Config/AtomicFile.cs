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
    public static async Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
            await using (stream.ConfigureAwait(false))
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
}
