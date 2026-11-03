using BlazeHarness.Core.Web;
using BlazeHarness.Core.Web.Content;
using BlazeHarness.Tests.Shared.Office;

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
}
