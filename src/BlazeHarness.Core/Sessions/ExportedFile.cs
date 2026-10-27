namespace BlazeHarness.Core.Sessions;

/// <summary>
/// A chat saved as a file.
/// </summary>
/// <param name="FileName">A name for the file, with its extension.</param>
/// <param name="ContentType">The media type of the content.</param>
/// <param name="Content">The text of the file.</param>
public sealed record ExportedFile(string FileName, string ContentType, string Content);
