namespace BlazeHarness.Core.Config;

/// <summary>
/// Writes a file so a crash never leaves it half written: the content goes to a temp file first, then replaces the target.
/// </summary>
internal static class AtomicFile
{
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
            await File.WriteAllTextAsync(temp, content, cancellationToken);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }
    }
}
