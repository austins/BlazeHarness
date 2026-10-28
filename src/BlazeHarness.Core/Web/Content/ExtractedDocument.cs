namespace BlazeHarness.Core.Web.Content;

/// <summary>
/// The readable form of a downloaded document.
/// </summary>
/// <param name="Title">The title, or an empty string when there is none.</param>
/// <param name="Text">The content as Markdown or plain text.</param>
/// <param name="Headings">The headings in order, each as a Markdown heading line, for an outline of a long document.</param>
/// <param name="Notice">A warning for the model about the quality of the text, or null.</param>
internal sealed record ExtractedDocument(string Title, string Text, IReadOnlyList<string> Headings, string? Notice = null);
