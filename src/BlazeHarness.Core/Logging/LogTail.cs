using System.Text;

namespace BlazeHarness.Core.Logging;

/// <summary>
/// Reads the end of the newest log file, for the diagnostics page.
/// </summary>
public static class LogTail
{
    private const int MaxReadBytes = 256 * 1024;

    /// <summary>
    /// Read the last lines of the newest log file.
    /// </summary>
    /// <remarks>
    /// Only the last 256 KB of the file are read, so a large file doesn't cost much.
    /// The file may be open for writing by the logger.
    /// </remarks>
    /// <param name="directory">The log folder.</param>
    /// <param name="maxLines">The most lines to return.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The lines, oldest first; empty when there is no log file or it can't be read.</returns>
    public static async Task<IReadOnlyList<string>> ReadAsync(string directory, int maxLines, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLines);

        try
        {
            var newest = Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, FileLoggerProvider.FilePrefix + "*" + FileLoggerProvider.FileExtension).Order(StringComparer.Ordinal).LastOrDefault()
                : null;
            if (newest is null)
            {
                return [];
            }

            await using var stream = new FileStream(newest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
            var cut = stream.Length > MaxReadBytes;
            if (cut)
            {
                stream.Seek(-MaxReadBytes, SeekOrigin.End);
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            var lines = (await reader.ReadToEndAsync(cancellationToken)).Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // The first line may start before the part that was read.
            var start = cut ? 1 : 0;
            return lines.Skip(start).TakeLast(maxLines).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
