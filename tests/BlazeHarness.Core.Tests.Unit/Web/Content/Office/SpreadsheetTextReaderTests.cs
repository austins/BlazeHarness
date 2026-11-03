#pragma warning disable S3220 // The SDK's element constructors take params children; the IEnumerable overload it reports is not the intended call.
using BlazeHarness.Core.Web.Content.Office;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace BlazeHarness.Core.Tests.Unit.Web.Content.Office;

public sealed class SpreadsheetTextReaderTests
{
    [Fact]
    public void Read_SharedAndInlineStrings_BecomeATable()
    {
        // Arrange
        var file = OfficeFiles.Spreadsheet(
            [("Data", false, [OfficeFiles.Row(OfficeFiles.SharedCell("A1", 0), OfficeFiles.SharedCell("B1", 1)), OfficeFiles.Row(OfficeFiles.InlineCell("A2", "Ann"), OfficeFiles.NumberCell("B2", "30"))])],
            ["Name", "Age"]);

        // Act
        var (text, _) = Read(file);

        // Assert
        text.Should().Be("## Data\n\n| Name | Age |\n| --- | --- |\n| Ann | 30 |");
    }

    [Fact]
    public void Read_SparseCells_KeepTheirColumns()
    {
        // Arrange
        var file = OfficeFiles.Spreadsheet([("S", false, [OfficeFiles.Row(OfficeFiles.InlineCell("A1", "a"), OfficeFiles.InlineCell("C1", "c"))])]);

        // Act
        var (text, _) = Read(file);

        // Assert
        text.Should().Contain("| a |  | c |");
    }

    [Fact]
    public void Read_HiddenSheet_IsSkipped()
    {
        // Arrange
        var file = OfficeFiles.Spreadsheet(
            [("Secret", true, [OfficeFiles.Row(OfficeFiles.InlineCell("A1", "hidden"))]), ("Shown", false, [OfficeFiles.Row(OfficeFiles.InlineCell("A1", "visible"))])]);

        // Act
        var (text, _) = Read(file);

        // Assert
        text.Should().Contain("visible").And.NotContain("hidden").And.NotContain("Secret");
    }

    [Fact]
    public void Read_DateStyledNumber_IsShownAsDate()
    {
        // Arrange
        var styles = new S.Stylesheet(new S.CellFormats(new S.CellFormat { NumberFormatId = 0 }, new S.CellFormat { NumberFormatId = 14 }));
        var file = OfficeFiles.Spreadsheet(
            [("S", false, [OfficeFiles.Row(OfficeFiles.InlineCell("A1", "When"), OfficeFiles.InlineCell("B1", "N")), OfficeFiles.Row(OfficeFiles.NumberCell("A2", "45000", 1), OfficeFiles.NumberCell("B2", "45000", 0))])],
            styles: styles);

        // Act
        var (text, _) = Read(file);

        // Assert
        text.Should().Contain("| 2023-03-15 | 45000 |");
    }

    [Fact]
    public void Read_CustomDateFormat_IsShownAsDate()
    {
        // Arrange
        var styles = new S.Stylesheet(
            new S.NumberingFormats(new S.NumberingFormat { NumberFormatId = 164, FormatCode = "yyyy\\-mm\\-dd hh:mm" }),
            new S.CellFormats(new S.CellFormat { NumberFormatId = 164 }));
        var file = OfficeFiles.Spreadsheet(
            [("S", false, [OfficeFiles.Row(OfficeFiles.InlineCell("A1", "When")), OfficeFiles.Row(OfficeFiles.NumberCell("A2", "45000.5", 0))])],
            styles: styles);

        // Act
        var (text, _) = Read(file);

        // Assert
        text.Should().Contain("| 2023-03-15 12:00:00 |");
    }

    [Fact]
    public void Read_Boolean_IsUppercase()
    {
        // Arrange
        var file = OfficeFiles.Spreadsheet([("S", false, [OfficeFiles.Row(OfficeFiles.InlineCell("A1", "Flag")), OfficeFiles.Row(OfficeFiles.BooleanCell("A2", true))])]);

        // Act
        var (text, _) = Read(file);

        // Assert
        text.Should().Contain("| TRUE |");
    }

    [Fact]
    public void Read_MoreRowsThanTheLimit_AddsANotice()
    {
        // Arrange
        var rows = Enumerable.Range(1, SpreadsheetTextReader.MaxRowsPerSheet + 5)
            .Select(i => OfficeFiles.Row(OfficeFiles.InlineCell($"A{i}", "x")))
            .ToArray();
        var file = OfficeFiles.Spreadsheet([("S", false, rows)]);

        // Act
        var (_, notice) = Read(file);

        // Assert
        notice.Should().Contain("rows");
    }

    private static (string Text, string? Notice) Read(byte[] file)
    {
        using var stream = new MemoryStream(file);
        using var document = SpreadsheetDocument.Open(stream, false);
        var output = new OfficeTextBuilder();
        var notice = SpreadsheetTextReader.Read(document, output, TestContext.Current.CancellationToken);
        return (output.ToString(), notice);
    }
}
