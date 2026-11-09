using BlazeHarness.Core.Web;
using BlazeHarness.Core.Web.Content;
using BlazeHarness.Tests.Shared.Office;
using System.Buffers.Binary;
using System.IO.Compression;

namespace BlazeHarness.Core.Tests.Unit.Web.Content;

public sealed class OpenXmlTextExtractorTests
{
    private readonly OpenXmlTextExtractor _sut = new();

    [Fact]
    public void Extract_Word_ReturnsTitleAndText()
    {
        // Arrange
        var file = OfficeFiles.Word([OfficeFiles.Paragraph("Hello")], title: " My doc ");

        // Act
        var result = _sut.Extract(file, TestContext.Current.CancellationToken);

        // Assert
        result.Title.Should().Be("My doc");
        result.Text.Should().Be("Hello");
        result.Notice.Should().BeNull();
    }

    [Fact]
    public void Extract_Spreadsheet_ReturnsSheets()
    {
        // Arrange
        var file = OfficeFiles.Spreadsheet([("S", false, [OfficeFiles.Row(OfficeFiles.InlineCell("A1", "x"))])]);

        // Act
        var result = _sut.Extract(file, TestContext.Current.CancellationToken);

        // Assert
        result.Text.Should().Contain("## S").And.Contain("| x |");
    }

    [Fact]
    public void Extract_Presentation_ReturnsSlides()
    {
        // Arrange
        var file = OfficeFiles.Presentation([new SlideSpec("Title", ["Body"])]);

        // Act
        var result = _sut.Extract(file, TestContext.Current.CancellationToken);

        // Assert
        result.Text.Should().Contain("## Slide 1: Title").And.Contain("- Body");
        result.Headings.Should().ContainSingle();
    }

    [Fact]
    public void Extract_DocumentWithoutText_Throws()
    {
        // Arrange
        var file = OfficeFiles.Word([OfficeFiles.Paragraph(" ")]);

        // Act
        var act = () => _sut.Extract(file, TestContext.Current.CancellationToken);

        // Assert
        act.Should().Throw<WebFetchException>().WithMessage("*no text*");
    }

    [Fact]
    public void Extract_OldOfficeFormat_ThrowsWithAHint()
    {
        // Act
        var act = () => _sut.Extract([0xD0, 0xCF, 0x11, 0xE0, 0, 0], TestContext.Current.CancellationToken);

        // Assert
        act.Should().Throw<WebFetchException>().WithMessage("*password-protected or in an old*");
    }

    [Fact]
    public void Extract_NotAZipFile_Throws()
    {
        // Act
        var act = () => _sut.Extract([1, 2, 3, 4], TestContext.Current.CancellationToken);

        // Assert
        act.Should().Throw<WebFetchException>().WithMessage("*could not be read*");
    }

    [Fact]
    public void Extract_ZipWithoutOfficeParts_Throws()
    {
        // Act
        var act = () => _sut.Extract(OfficeFiles.ZipWithEntry("readme.txt"), TestContext.Current.CancellationToken);

        // Assert
        act.Should().Throw<WebFetchException>().WithMessage("*not a Word, Excel or PowerPoint*");
    }

    [Fact]
    public void Extract_SpreadsheetAndPresentation_ReturnTheirTitles()
    {
        // Arrange
        var sheet = OfficeFiles.Spreadsheet([("S", false, [OfficeFiles.Row(OfficeFiles.InlineCell("A1", "x"))])], title: "Budget");
        var slides = OfficeFiles.Presentation([new SlideSpec("T", ["Body"])], title: "Deck");

        // Act
        var sheetResult = _sut.Extract(sheet, TestContext.Current.CancellationToken);
        var slidesResult = _sut.Extract(slides, TestContext.Current.CancellationToken);

        // Assert
        sheetResult.Title.Should().Be("Budget");
        slidesResult.Title.Should().Be("Deck");
    }

    [Fact]
    public void Extract_PresentationWithoutSlides_Throws()
    {
        // Act
        var act = () => _sut.Extract(OfficeFiles.Presentation([]), TestContext.Current.CancellationToken);

        // Assert
        act.Should().Throw<WebFetchException>().WithMessage("*no text*");
    }

    [Fact]
    public void Extract_ZipWithTooManyEntries_Throws()
    {
        // Arrange
        var file = OfficeFiles.ZipWithEntries(OpenXmlTextExtractor.MaxEntries + 1);

        // Act
        var act = () => _sut.Extract(file, TestContext.Current.CancellationToken);

        // Assert
        act.Should().Throw<WebFetchException>().WithMessage("*too large*");
    }

    [Fact]
    public void Extract_ZipThatUnpacksToTooMuch_Throws()
    {
        // Arrange
        var file = OfficeFiles.ZipWithLargeEntry(OpenXmlTextExtractor.MaxUncompressedBytes + 1);

        // Act
        var act = () => _sut.Extract(file, TestContext.Current.CancellationToken);

        // Assert
        act.Should().Throw<WebFetchException>().WithMessage("*too large*");
    }

    [Fact]
    public void Extract_VeryLongDocument_StopsWithANotice()
    {
        // Arrange
        var text = new string('x', 10_000);
        var paragraphs = Enumerable.Range(0, 300).Select(_ => OfficeFiles.Paragraph(text));
        var file = OfficeFiles.Word(paragraphs);

        // Act
        var result = _sut.Extract(file, TestContext.Current.CancellationToken);

        // Assert
        result.Notice.Should().Contain("characters");
        result.Text.Length.Should().BeLessThan(2_100_000);
    }

    [Fact]
    public void Extract_WordPartWithBrokenXml_Throws()
    {
        var file = OfficeFiles.WithBrokenEntry(OfficeFiles.Word([OfficeFiles.Paragraph("text")]), "word/document.xml");

        // Act
        var act = () => _sut.Extract(file, TestContext.Current.CancellationToken);

        // Assert
        act.Should().Throw<WebFetchException>().WithMessage("*could not be read*");
    }

    [Fact]
    public void Extract_DeeplyNestedWordPart_ThrowsInsteadOfOverflowingTheStack()
    {
        // Arrange
        var file = WithNestedRun(OfficeFiles.Word([OfficeFiles.Paragraph("text")]), 100_000);

        // Act
        var act = () => _sut.Extract(file, TestContext.Current.CancellationToken);

        // Assert
        act.Should().Throw<WebFetchException>().WithMessage("*nested too deeply*");
    }

    [Fact]
    public void Extract_NestingBelowTheLimit_ReadsTheText()
    {
        // Arrange
        var file = WithNestedRun(OfficeFiles.Word([OfficeFiles.Paragraph("text")]), 100);

        // Act
        var result = _sut.Extract(file, TestContext.Current.CancellationToken);

        // Assert
        result.Text.Should().Be("text");
    }

    [Fact]
    public void Extract_EntriesSharingOnePayload_Throws()
    {
        // Arrange
        var file = WithRepeatedEntry(10);

        // Act
        var act = () => _sut.Extract(file, TestContext.Current.CancellationToken);

        // Assert
        act.Should().Throw<WebFetchException>().WithMessage("*could not be read*");
    }

    [Fact]
    public void Extract_Cancelled_ThrowsOperationCanceled()
    {
        // Arrange
        var file = OfficeFiles.Word([OfficeFiles.Paragraph("Hello")]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = () => _sut.Extract(file, cts.Token);

        // Assert
        act.Should().Throw<OperationCanceledException>();
    }

    /// <summary>
    /// Builds a zip whose central directory lists one stored entry <paramref name="copies"/> times, every copy pointing at the same data.
    /// </summary>
    /// <param name="copies">How many times to list the entry.</param>
    private static byte[] WithRepeatedEntry(int copies)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("part.xml", CompressionLevel.NoCompression).Open());
            writer.Write("<a>" + new string('x', 1_000) + "</a>");
        }

        var zip = stream.ToArray();
        const int endRecordLength = 22;
        var end = zip.AsSpan(zip.Length - endRecordLength);
        var directorySize = BinaryPrimitives.ReadInt32LittleEndian(end[12..]);
        var directoryOffset = BinaryPrimitives.ReadInt32LittleEndian(end[16..]);
        var record = zip.AsSpan(directoryOffset, directorySize).ToArray();

        var newEnd = end.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(newEnd.AsSpan(8), (ushort)copies);
        BinaryPrimitives.WriteUInt16LittleEndian(newEnd.AsSpan(10), (ushort)copies);
        BinaryPrimitives.WriteInt32LittleEndian(newEnd.AsSpan(12), directorySize * copies);
        return [.. zip.AsSpan(0, directoryOffset), .. Enumerable.Repeat(record, copies).SelectMany(bytes => bytes), .. newEnd];
    }

    /// <summary>
    /// Wraps the first run of a Word file's body in <paramref name="depth"/> nested tracked insertions.
    /// </summary>
    /// <param name="file">The Word file.</param>
    /// <param name="depth">How many elements to nest the run in.</param>
    private static byte[] WithNestedRun(byte[] file, int depth)
    {
        using var stream = new MemoryStream();
        stream.Write(file);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("word/document.xml")!;
            string xml;
            using (var reader = new StreamReader(entry.Open()))
            {
                xml = reader.ReadToEnd();
            }

            entry.Delete();
            var start = xml.IndexOf("<w:r>", StringComparison.Ordinal);
            var end = xml.IndexOf("</w:r>", start, StringComparison.Ordinal) + "</w:r>".Length;
            var open = string.Concat(Enumerable.Repeat("<w:ins w:id=\"1\" w:author=\"a\">", depth));
            var close = string.Concat(Enumerable.Repeat("</w:ins>", depth));
            using var writer = new StreamWriter(archive.CreateEntry("word/document.xml").Open());
            writer.Write(xml[..start] + open + xml[start..end] + close + xml[end..]);
        }

        return stream.ToArray();
    }
}
