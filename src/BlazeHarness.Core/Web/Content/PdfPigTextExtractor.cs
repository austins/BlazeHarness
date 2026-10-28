using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace BlazeHarness.Core.Web.Content;

/// <summary>
/// Reads PDF text with the PdfPig library.
/// </summary>
internal sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    internal const int MaxPages = 300;
    internal const int MaxCharacters = 4_000_000;

    /// <inheritdoc />
    public ExtractedDocument Extract(byte[] pdf, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        try
        {
            using var document = PdfDocument.Open(pdf, new ParsingOptions { UseLenientParsing = true });
            var builder = new StringBuilder();
            var pages = document.NumberOfPages;
            var shown = 0;
            foreach (var page in document.GetPages().Take(MaxPages))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = ContentOrderTextExtractor.GetText(page, addDoubleNewline: true).Trim();
                builder.Append("[Page ").Append(page.Number).Append("]\n\n").Append(text).Append("\n\n");
                shown++;
                if (builder.Length > MaxCharacters)
                {
                    break;
                }
            }

            if (builder.Length == 0 || string.IsNullOrWhiteSpace(StripMarkers(builder.ToString())))
            {
                throw new WebFetchException("The PDF has no text in it. It may be scanned images, which this tool can't read.");
            }

            var notice = shown < pages ? $"Only the first {shown} of {pages} pages were read." : null;
            return new ExtractedDocument(document.Information.Title?.Trim() ?? string.Empty, builder.ToString().TrimEnd(), [], notice);
        }
        catch (Exception ex) when (ex is PdfDocumentEncryptedException)
        {
            throw new WebFetchException("The PDF is protected by a password.");
        }
        catch (Exception ex) when (ex is not (WebFetchException or OperationCanceledException))
        {
            throw new WebFetchException("The PDF could not be read.");
        }
    }

    private static string StripMarkers(string text)
    {
        return string.Concat(text.Split('\n').Where(line => !line.StartsWith("[Page ", StringComparison.Ordinal)));
    }
}
