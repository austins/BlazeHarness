namespace BlazeHarness.Core.Attachments;

/// <summary>
/// A file saved in a session's attachments.
/// </summary>
/// <param name="Id">The short identifier the file is referenced by.</param>
/// <param name="Name">The file name, cleaned of any path.</param>
/// <param name="MediaType">The type decided from the content, such as <c>image/png</c> or <c>text/plain</c>.</param>
/// <param name="Size">The size in bytes.</param>
/// <param name="Lines">The number of lines for a text file, or zero for an image.</param>
public sealed record AttachmentInfo(string Id, string Name, string MediaType, long Size, int Lines)
{
    /// <summary>
    /// Gets a value indicating whether the file is an image.
    /// </summary>
    public bool IsImage => MediaType.StartsWith("image/", StringComparison.Ordinal);
}
