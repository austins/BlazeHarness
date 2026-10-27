namespace BlazeHarness.Core.Tools;

/// <summary>
/// Writes a file so a reader never sees half of it.
/// </summary>
internal static class AtomicFile
{
    /// <summary>
    /// Write bytes to a temporary file next to the target and move it into place, keeping the target's Unix permissions.
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
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
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
}
