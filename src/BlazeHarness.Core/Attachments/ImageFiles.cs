using System.Text;

namespace BlazeHarness.Core.Attachments;

/// <summary>
/// Recognizes image files from their content, not from their name.
/// </summary>
public static class ImageFiles
{
    /// <summary>
    /// The number of bytes at the start of a file that decide its type.
    /// </summary>
    public const int HeadBytes = 512;

    /// <summary>
    /// The number of bytes <see cref="SniffRaster"/> needs to recognize every format it knows.
    /// </summary>
    public const int SniffBytes = 12;

    /// <summary>
    /// The largest image the chat shows from a folder, in bytes.
    /// </summary>
    public const long MaxDisplayBytes = 10L * 1024 * 1024;

    /// <summary>
    /// Recognize PNG, JPEG, GIF and WebP, the formats the common vision APIs take.
    /// </summary>
    /// <param name="head">The first bytes of the file.</param>
    /// <returns>The media type and the matching file extension, or null when it is none of them.</returns>
    public static (string MediaType, string Extension)? SniffRaster(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith(PngSignature))
        {
            return ("image/png", ".png");
        }

        if (head.StartsWith(JpegSignature))
        {
            return ("image/jpeg", ".jpg");
        }

        if (head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8))
        {
            return ("image/gif", ".gif");
        }

        return head.Length >= SniffBytes && head.StartsWith("RIFF"u8) && head[8..].StartsWith("WEBP"u8) ? ("image/webp", ".webp") : null;
    }

    /// <summary>
    /// Recognize an image every current browser displays: PNG, JPEG, GIF, WebP, AVIF and SVG.
    /// </summary>
    /// <remarks>
    /// An SVG is only recognized when its name ends in <c>.svg</c> and its start holds an <c>&lt;svg</c> element, since it is text.
    /// </remarks>
    /// <param name="path">The file to look at.</param>
    /// <returns>The media type, or null when the file is not such an image or can't be read.</returns>
    public static string? DetectDisplayable(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        Span<byte> buffer = stackalloc byte[HeadBytes];
        int read;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            read = stream.Read(buffer);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var head = buffer[..read];
        if (SniffRaster(head) is { } raster)
        {
            return raster.MediaType;
        }

        if (head.Length >= SniffBytes && head[4..].StartsWith("ftyp"u8) && (head[8..].StartsWith("avif"u8) || head[8..].StartsWith("avis"u8)))
        {
            return "image/avif";
        }

        var isSvgName = string.Equals(Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase);
        return isSvgName && Encoding.UTF8.GetString(head).Contains("<svg", StringComparison.OrdinalIgnoreCase) ? "image/svg+xml" : null;
    }

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];
}
