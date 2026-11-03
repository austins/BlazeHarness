using BlazeHarness.Core.Web.Content.Office;
using BlazeHarness.Tests.Shared.Office;
using DocumentFormat.OpenXml.Packaging;

namespace BlazeHarness.Core.Tests.Unit.Web.Content.Office;

public sealed class PresentationTextReaderTests
{
    [Fact]
    public void Read_Slides_HaveTitleBodyAndNotes()
    {
        // Arrange
        var file = OfficeFiles.Presentation(
        [
            new SlideSpec("Welcome", ["First point", "Second point"], "Say hello"),
            new SlideSpec(Lines: ["Only body"]),
        ]);

        // Act
        var output = Read(file);

        // Assert
        output.ToString().Should().Be("## Slide 1: Welcome\n\n- First point\n- Second point\n\nNotes: Say hello\n\n## Slide 2\n\n- Only body");
        output.Headings.Should().Equal("## Slide 1: Welcome", "## Slide 2");
    }

    [Fact]
    public void Read_Table_BecomesMarkdownTable()
    {
        // Arrange
        var file = OfficeFiles.Presentation([new SlideSpec("Data", Table: [["A", "B"], ["1", "2"]])]);

        // Act
        var output = Read(file);

        // Assert
        output.ToString().Should().Contain("| A | B |\n| --- | --- |\n| 1 | 2 |");
    }

    [Fact]
    public void Read_GroupedShape_IsReadToo()
    {
        // Arrange
        var file = OfficeFiles.Presentation([new SlideSpec(Lines: ["Inside a group"], Grouped: true)]);

        // Act
        var output = Read(file);

        // Assert
        output.ToString().Should().Contain("- Inside a group");
    }

    [Fact]
    public void Read_NoSlides_WritesNothing()
    {
        // Arrange
        var file = OfficeFiles.Presentation([]);

        // Act
        var output = Read(file);

        // Assert
        output.ToString().Should().BeEmpty();
    }

    private static OfficeTextBuilder Read(byte[] file)
    {
        using var stream = new MemoryStream(file);
        using var document = PresentationDocument.Open(stream, false);
        var output = new OfficeTextBuilder();
        PresentationTextReader.Read(document, output, TestContext.Current.CancellationToken);
        return output;
    }
}
