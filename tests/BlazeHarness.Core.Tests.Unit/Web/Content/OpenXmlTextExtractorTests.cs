using BlazeHarness.Core.Tests.Unit.Web.Content.Office;
using BlazeHarness.Core.Web;
using BlazeHarness.Core.Web.Content;

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
        var file = OfficeFiles.Presentation(("Title", ["Body"], null, null));

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
