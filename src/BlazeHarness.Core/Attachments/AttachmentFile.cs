namespace BlazeHarness.Core.Attachments;

/// <summary>
/// A saved attachment on disk.
/// </summary>
/// <param name="Path">The full path of the stored file.</param>
/// <param name="Name">The cleaned file name.</param>
/// <param name="MediaType">The media type to serve it with.</param>
public sealed record AttachmentFile(string Path, string Name, string MediaType);
