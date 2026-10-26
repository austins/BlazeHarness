using System.Globalization;

namespace BlazeHarness.Core.Attachments;

/// <summary>
/// Writes file sizes the way people read them.
/// </summary>
public static class FileSize
{
    /// <summary>
    /// Format a size in bytes, such as <c>512 bytes</c>, <c>1.5 KB</c> or <c>3 MB</c>.
    /// </summary>
    /// <param name="bytes">The size in bytes.</param>
    /// <returns>The size with a unit.</returns>
    public static string Format(long bytes)
    {
        return bytes switch
        {
            < 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes} bytes"),
            < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.#} MB"),
        };
    }
}
