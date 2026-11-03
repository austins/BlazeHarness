namespace BlazeHarness.Core.Web.Content;

/// <summary>
/// Knows the content types of the Office files the fetch tool reads.
/// </summary>
internal static class OfficeMediaTypes
{
    private static readonly string[] Known =
    [
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
    ];

    /// <summary>
    /// Get whether a content type is that of a docx, xlsx or pptx file.
    /// </summary>
    /// <param name="mediaType">The content type without parameters, or null.</param>
    /// <returns>True for a Word, Excel or PowerPoint Open XML type.</returns>
    public static bool IsOffice(string? mediaType)
    {
        return mediaType is not null && Array.Exists(Known, known => known.Equals(mediaType, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Get whether a content type is one a server may use for any file, so the bytes decide what it is.
    /// </summary>
    /// <param name="mediaType">The content type without parameters, or null.</param>
    /// <returns>True when the type is missing, a generic binary type or a zip type.</returns>
    public static bool IsGenericBinary(string? mediaType)
    {
        return string.IsNullOrEmpty(mediaType)
            || mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/zip", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/x-zip-compressed", StringComparison.OrdinalIgnoreCase);
    }
}
