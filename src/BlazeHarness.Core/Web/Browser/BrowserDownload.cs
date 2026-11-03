namespace BlazeHarness.Core.Web.Browser;

/// <summary>
/// A file the page offered for download and the browser saved.
/// </summary>
/// <param name="FileName">The name the page suggested for the file.</param>
/// <param name="Bytes">The size of the saved file.</param>
internal sealed record BrowserDownload(string FileName, long Bytes);
