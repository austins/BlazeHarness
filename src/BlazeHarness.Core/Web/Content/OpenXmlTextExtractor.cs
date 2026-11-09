using BlazeHarness.Core.Web.Content.Office;
using DocumentFormat.OpenXml.Packaging;
using System.Globalization;
using System.IO.Compression;
using System.Xml;

namespace BlazeHarness.Core.Web.Content;

/// <summary>
/// Reads Word, Excel and PowerPoint files with the Open XML SDK.
/// </summary>
internal sealed class OpenXmlTextExtractor : IOfficeTextExtractor
{
    internal const int MaxEntries = 5_000;
    internal const long MaxUncompressedBytes = 200L * 1024 * 1024;

    /// <summary>
    /// The deepest element nesting accepted in any part of the file.
    /// </summary>
    /// <remarks>
    /// The Open XML SDK loads elements recursively, so a part nested thousands of levels deep overflows the stack
    /// and ends the process. Real documents stay far below this.
    /// </remarks>
    internal const int MaxXmlDepth = 256;

    private static readonly byte[] OleMagic = [0xD0, 0xCF, 0x11, 0xE0];

    private static readonly XmlReaderSettings ScanSettings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };

    /// <inheritdoc />
    public ExtractedDocument Extract(byte[] document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.AsSpan().StartsWith(OleMagic))
        {
            throw new WebFetchException("The file is password-protected or in an old Office format (doc, xls or ppt), which this tool can't read.");
        }

        try
        {
            using var stream = new MemoryStream(document, writable: false);
            var format = FormatOf(stream, cancellationToken);
            stream.Position = 0;
            var output = new OfficeTextBuilder();
            string? notice = null;
            string title;
            switch (format)
            {
                case Format.Word:
                    using (var word = WordprocessingDocument.Open(stream, false))
                    {
                        WordTextReader.Read(word, output, cancellationToken);
                        title = word.PackageProperties.Title ?? string.Empty;
                    }

                    break;
                case Format.Spreadsheet:
                    using (var sheets = SpreadsheetDocument.Open(stream, false))
                    {
                        notice = SpreadsheetTextReader.Read(sheets, output, cancellationToken);
                        title = sheets.PackageProperties.Title ?? string.Empty;
                    }

                    break;
                default:
                    using (var slides = PresentationDocument.Open(stream, false))
                    {
                        PresentationTextReader.Read(slides, output, cancellationToken);
                        title = slides.PackageProperties.Title ?? string.Empty;
                    }

                    break;
            }

            if (!output.HasText)
            {
                throw new WebFetchException("The document has no text in it.");
            }

            if (output.Full)
            {
                notice = string.Create(CultureInfo.InvariantCulture, $"Only the first {OfficeTextBuilder.MaxCharacters} characters were read.");
            }

            return new ExtractedDocument(title.Trim(), output.ToString(), output.Headings, notice);
        }
        catch (Exception ex) when (ex is not (WebFetchException or OperationCanceledException))
        {
            throw new WebFetchException("The document could not be read.");
        }
    }

    private static Format FormatOf(MemoryStream stream, CancellationToken cancellationToken)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.Entries.Count > MaxEntries || zip.Entries.Sum(entry => entry.Length) > MaxUncompressedBytes)
        {
            throw new WebFetchException("The document is too large to read.");
        }

        // Entries whose stored sizes add up to more than the file overlap. A stored entry reads its stored size whatever
        // size it declares, so overlapping entries would let the scan below read far more than the limits above allow.
        if (zip.Entries.Sum(entry => entry.CompressedLength) > stream.Length)
        {
            throw new WebFetchException("The document could not be read.");
        }

        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TooDeep(entry))
            {
                throw new WebFetchException("The document is nested too deeply to read.");
            }
        }

        var names = zip.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Contains("word/document.xml"))
        {
            return Format.Word;
        }

        if (names.Contains("xl/workbook.xml"))
        {
            return Format.Spreadsheet;
        }

        return names.Contains("ppt/presentation.xml")
            ? Format.Presentation
            : throw new WebFetchException("The file is not a Word, Excel or PowerPoint document.");
    }

    /// <summary>
    /// Tell whether an entry's XML nests elements deeper than <see cref="MaxXmlDepth"/>.
    /// </summary>
    /// <remarks>
    /// Every entry is scanned whatever its name, since a part can have any name. An entry that isn't XML, such as an image,
    /// stops at its first parse error; whether it matters is left to the SDK.
    /// </remarks>
    /// <param name="entry">The zip entry to scan.</param>
    private static bool TooDeep(ZipArchiveEntry entry)
    {
        using var content = entry.Open();
        using var reader = XmlReader.Create(content, ScanSettings);
        try
        {
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.Depth >= MaxXmlDepth)
                {
                    return true;
                }
            }
        }
        catch (XmlException)
        {
            return false;
        }

        return false;
    }

    private enum Format
    {
        Word,
        Spreadsheet,
        Presentation,
    }
}
