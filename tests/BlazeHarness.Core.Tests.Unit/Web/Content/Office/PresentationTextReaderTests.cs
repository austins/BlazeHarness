using BlazeHarness.Core.Web.Content.Office;
using DocumentFormat.OpenXml.Packaging;

namespace BlazeHarness.Core.Tests.Unit.Web.Content.Office;

public sealed class PresentationTextReaderTests
{
    [Fact]
    public void Read_Slides_HaveTitleBodyAndNotes()
    {
        // Arrange
        var file = OfficeFiles.Presentation(
            ("Welcome", ["First point", "Second point"], "Say hello", null),
            (null, ["Only body"], null, null));

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
        var file = OfficeFiles.Presentation(("Data", [], null, [["A", "B"], ["1", "2"]]));

        // Act
        var output = Read(file);

        // Assert
        output.ToString().Should().Contain("| A | B |\n| --- | --- |\n| 1 | 2 |");
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
