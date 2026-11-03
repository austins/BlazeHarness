#pragma warning disable S3220 // The SDK's element constructors take params children; the IEnumerable overload it reports is not the intended call.
using BlazeHarness.Core.Web.Content.Office;
using BlazeHarness.Tests.Shared.Office;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

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

    [Fact]
    public void Read_TitleStyle_IsAHeadingOne()
    {
        // Arrange
        var file = OfficeFiles.Word([OfficeFiles.Paragraph("My report", "Title")], new Dictionary<string, string> { ["Title"] = "Title" });

        // Act
        var output = Read(file);

        // Assert
        output.ToString().Should().Be("# My report");
    }

    [Fact]
    public void Read_ContentControl_IsReadThrough()
    {
        // Arrange
        var file = OfficeFiles.Word([new W.SdtBlock(new W.SdtContentBlock(OfficeFiles.Paragraph("Inside a control")))]);

        // Act
        var output = Read(file);

        // Assert
        output.ToString().Should().Be("Inside a control");
    }

    [Fact]
    public void Read_TabAndBreak_BecomeTabAndSpace()
    {
        // Arrange
        var file = OfficeFiles.Word([OfficeFiles.RawParagraph(new W.Run(new W.Text("a"), new W.TabChar(), new W.Text("b"), new W.Break(), new W.Text("c")))]);

        // Act
        var output = Read(file);

        // Assert
        output.ToString().Should().Be("a\tb c");
    }

    [Fact]
    public void Read_AlternateContent_IsNotRepeatedFromItsFallback()
    {
        // Arrange
        var content = new AlternateContent(
            new AlternateContentChoice(new W.Run(new W.Text("choice"))) { Requires = "wps" },
            new AlternateContentFallback(new W.Run(new W.Text("fallback"))));
        var file = OfficeFiles.Word([OfficeFiles.RawParagraph(content)]);

        // Act
        var output = Read(file);

        // Assert
        output.ToString().Should().Be("choice");
    }

    [Fact]
    public void Read_TableCellWithSeveralParagraphs_JoinsThemWithASpace()
    {
        // Arrange
        var cell = new W.TableCell(OfficeFiles.Paragraph("one"), OfficeFiles.Paragraph("two"));
        var file = OfficeFiles.Word([new W.Table(new W.TableRow(cell, new W.TableCell(OfficeFiles.Paragraph("x"))))]);

        // Act
        var output = Read(file);

        // Assert
        output.ToString().Should().Contain("| one two | x |");
    }

    [Fact]
    public void Read_Document_WithoutABody_WritesNothing()
    {
        // Arrange
        var file = OfficeFiles.Word([]);

        // Act
        var output = Read(file);

        // Assert
        output.ToString().Should().BeEmpty();
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
