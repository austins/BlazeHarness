using BlazeHarness.Core.Web.Content.Office;
using DocumentFormat.OpenXml.Packaging;

namespace BlazeHarness.Core.Tests.Unit.Web.Content.Office;

public sealed class WordTextReaderTests
{
    [Fact]
    public void Read_HeadingStyle_BecomesMarkdownHeading()
    {
        // Arrange
        var file = OfficeFiles.Word(
            [OfficeFiles.Paragraph("Intro", "Heading1"), OfficeFiles.Paragraph("Body text"), OfficeFiles.Paragraph("Sub", "Heading2")],
            new Dictionary<string, string> { ["Heading1"] = "heading 1", ["Heading2"] = "Heading 2" });

        // Act
        var output = Read(file);

        // Assert
        output.ToString().Should().Be("# Intro\n\nBody text\n\n## Sub");
        output.Headings.Should().Equal("# Intro", "## Sub");
    }

    [Fact]
    public void Read_LocalizedStyleWithOutlineLevel_UsesTheOutlineLevel()
    {
        // Arrange
        var file = OfficeFiles.Word([OfficeFiles.Paragraph("Kapitel", "Custom", outlineLevel: 1)]);

        // Act
        var output = Read(file);

        // Assert
        output.ToString().Should().Be("## Kapitel");
    }

    [Fact]
    public void Read_ListItems_AreBulletsWithIndent()
    {
        // Arrange
        var file = OfficeFiles.Word([OfficeFiles.Paragraph("One", listLevel: 0), OfficeFiles.Paragraph("Nested", listLevel: 1), OfficeFiles.Paragraph("After")]);

        // Act
        var output = Read(file);

        // Assert
        output.ToString().Should().Be("- One\n  - Nested\n\nAfter");
    }

    [Fact]
    public void Read_Table_BecomesMarkdownTable()
    {
        // Arrange
        var file = OfficeFiles.Word([OfficeFiles.WordTable(["Name", "Age"], ["Ann", "30"])]);

        // Act
        var output = Read(file);

        // Assert
        output.ToString().Should().Be("| Name | Age |\n| --- | --- |\n| Ann | 30 |");
    }

    [Fact]
    public void Read_EmptyParagraphs_AreSkipped()
    {
        // Arrange
        var file = OfficeFiles.Word([OfficeFiles.Paragraph("  "), OfficeFiles.Paragraph("Text")]);

        // Act
        var output = Read(file);

        // Assert
        output.ToString().Should().Be("Text");
    }

    private static OfficeTextBuilder Read(byte[] file)
    {
        using var stream = new MemoryStream(file);
        using var document = WordprocessingDocument.Open(stream, false);
        var output = new OfficeTextBuilder();
        WordTextReader.Read(document, output, TestContext.Current.CancellationToken);
        return output;
    }
}
