namespace BlazeHarness.Core.Web.Content;

/// <summary>
/// Reads the text of a PDF file.
/// </summary>
/// <remarks>
/// It is an interface so the PDF library can be replaced without touching the fetch tool.
/// </remarks>
internal interface IPdfTextExtractor
{
    /// <summary>
    /// Read the text of a PDF file.
    /// </summary>
    /// <param name="pdf">The file contents.</param>
    /// <param name="cancellationToken">A token that stops the reading between pages.</param>
    /// <returns>The text, with a marker line before each page.</returns>
    /// <exception cref="WebFetchException">The file is not a readable PDF, is protected by a password, or has no text.</exception>
    ExtractedDocument Extract(byte[] pdf, CancellationToken cancellationToken);
}
