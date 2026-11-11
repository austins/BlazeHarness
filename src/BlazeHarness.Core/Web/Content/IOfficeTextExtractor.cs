namespace BlazeHarness.Core.Web.Content;

/// <summary>
/// Reads the text of a Word, Excel or PowerPoint file.
/// </summary>
/// <remarks>
/// It is an interface so the Office library can be replaced without touching the fetch tool.
/// </remarks>
internal interface IOfficeTextExtractor
{
    /// <summary>
    /// Read the text of an Office Open XML file (docx, xlsx or pptx).
    /// </summary>
    /// <param name="document">The file contents.</param>
    /// <param name="cancellationToken">A token that stops the reading between blocks.</param>
    /// <returns>The text as Markdown: headings and tables are kept, and a spreadsheet or presentation has one section per sheet or slide.</returns>
    /// <exception cref="WebFetchException">The file is not a readable Office file, is protected by a password or in an old format, is too large, or has no text.</exception>
    public ExtractedDocument Extract(byte[] document, CancellationToken cancellationToken);
}
