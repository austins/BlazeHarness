#pragma warning disable S3220 // The SDK's element constructors take params children; the IEnumerable overload it reports is not the intended call.
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using D = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace BlazeHarness.Core.Tests.Unit.Web.Content.Office;

/// <summary>
/// Builds small docx, xlsx and pptx files in memory for the reader tests.
/// </summary>
internal static class OfficeFiles
{
    public static byte[] Word(IEnumerable<OpenXmlElement> blocks, IReadOnlyDictionary<string, string>? styleNames = null, string? title = null)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new W.Document(new W.Body(blocks));
            if (styleNames is not null)
            {
                main.AddNewPart<StyleDefinitionsPart>().Styles = new W.Styles(styleNames.Select(pair => new W.Style(new W.StyleName { Val = pair.Value }) { StyleId = pair.Key }));
            }

            if (title is not null)
            {
                document.PackageProperties.Title = title;
            }
        }

        return stream.ToArray();
    }

    public static W.Paragraph Paragraph(string text, string? styleId = null, int? listLevel = null, int? outlineLevel = null)
    {
        var properties = new W.ParagraphProperties();
        if (styleId is not null)
        {
            properties.ParagraphStyleId = new W.ParagraphStyleId { Val = styleId };
        }

        if (listLevel is { } level)
        {
            properties.NumberingProperties = new W.NumberingProperties(new W.NumberingLevelReference { Val = level }, new W.NumberingId { Val = 1 });
        }

        if (outlineLevel is { } outline)
        {
            properties.OutlineLevel = new W.OutlineLevel { Val = outline };
        }

        return new W.Paragraph(properties, new W.Run(new W.Text(text)));
    }

    public static W.Table WordTable(params string[][] rows)
    {
        return new W.Table(rows.Select(row => new W.TableRow(row.Select(cell => new W.TableCell(new W.Paragraph(new W.Run(new W.Text(cell))))))));
    }

    public static S.Cell InlineCell(string reference, string text)
    {
        return new S.Cell { CellReference = reference, DataType = S.CellValues.InlineString, InlineString = new S.InlineString(new S.Text(text)) };
    }

    public static S.Cell SharedCell(string reference, int index)
    {
        return new S.Cell { CellReference = reference, DataType = S.CellValues.SharedString, CellValue = new S.CellValue(index.ToString(System.Globalization.CultureInfo.InvariantCulture)) };
    }

    public static S.Cell NumberCell(string reference, string value, uint? style = null)
    {
        return new S.Cell { CellReference = reference, CellValue = new S.CellValue(value), StyleIndex = style };
    }

    public static S.Cell BooleanCell(string reference, bool value)
    {
        return new S.Cell { CellReference = reference, DataType = S.CellValues.Boolean, CellValue = new S.CellValue(value ? "1" : "0") };
    }

    public static S.Row Row(params S.Cell[] cells)
    {
        return new S.Row(cells);
    }

    public static byte[] Spreadsheet(IEnumerable<(string Name, bool Hidden, S.Row[] Rows)> sheets, string[]? sharedStrings = null, S.Stylesheet? styles = null)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new S.Workbook(new S.Sheets());
            if (sharedStrings is not null)
            {
                workbook.AddNewPart<SharedStringTablePart>().SharedStringTable = new S.SharedStringTable(sharedStrings.Select(text => new S.SharedStringItem(new S.Text(text))));
            }

            if (styles is not null)
            {
                workbook.AddNewPart<WorkbookStylesPart>().Stylesheet = styles;
            }

            uint id = 1;
            foreach (var (name, hidden, rows) in sheets)
            {
                var part = workbook.AddNewPart<WorksheetPart>();
                part.Worksheet = new S.Worksheet(new S.SheetData(rows));
                var sheet = new S.Sheet { Id = workbook.GetIdOfPart(part), SheetId = id++, Name = name };
                if (hidden)
                {
                    sheet.State = S.SheetStateValues.Hidden;
                }

                workbook.Workbook.Sheets!.Append(sheet);
            }
        }

        return stream.ToArray();
    }

    public static byte[] Presentation(params (string? Title, string[] Lines, string? Notes, string[][]? Table)[] slides)
    {
        using var stream = new MemoryStream();
        using (var document = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            var presentation = document.AddPresentationPart();
            presentation.Presentation = new P.Presentation(new P.SlideIdList());
            uint id = 256;
            foreach (var (title, lines, notes, table) in slides)
            {
                var slide = presentation.AddNewPart<SlidePart>();
                var tree = new P.ShapeTree();
                if (title is not null)
                {
                    tree.Append(TextShape(title, P.PlaceholderValues.Title));
                }

                if (lines.Length > 0)
                {
                    tree.Append(TextShape(lines, null));
                }

                if (table is not null)
                {
                    tree.Append(TableFrame(table));
                }

                slide.Slide = new P.Slide(new P.CommonSlideData(tree));
                if (notes is not null)
                {
                    var notesPart = slide.AddNewPart<NotesSlidePart>();
                    notesPart.NotesSlide = new P.NotesSlide(new P.CommonSlideData(new P.ShapeTree(TextShape([notes], P.PlaceholderValues.Body))));
                }

                presentation.Presentation.SlideIdList!.Append(new P.SlideId { Id = id++, RelationshipId = presentation.GetIdOfPart(slide) });
            }
        }

        return stream.ToArray();
    }

    public static byte[] ZipWithEntry(string name)
    {
        using var stream = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write("x");
        }

        return stream.ToArray();
    }

    private static P.GraphicFrame TableFrame(string[][] rows)
    {
        var table = new D.Table();
        foreach (var row in rows)
        {
            var tableRow = new D.TableRow();
            foreach (var cell in row)
            {
                tableRow.Append(new D.TableCell(new D.TextBody(new D.Paragraph(new D.Run(new D.Text(cell))))));
            }

            table.Append(tableRow);
        }

        return new P.GraphicFrame(new D.Graphic(new D.GraphicData(table)));
    }

    private static P.Shape TextShape(string text, P.PlaceholderValues? placeholder)
    {
        return TextShape([text], placeholder);
    }

    private static P.Shape TextShape(string[] lines, P.PlaceholderValues? placeholder)
    {
        var properties = new P.ApplicationNonVisualDrawingProperties();
        if (placeholder is { } type)
        {
            properties.Append(new P.PlaceholderShape { Type = type });
        }

        var body = new P.TextBody(new D.BodyProperties(), new D.ListStyle());
        body.Append(lines.Select(line => new D.Paragraph(new D.Run(new D.Text(line)))));
        return new P.Shape(
            new P.NonVisualShapeProperties(new P.NonVisualDrawingProperties { Id = 1, Name = "Shape" }, new P.NonVisualShapeDrawingProperties(), properties),
            new P.ShapeProperties(),
            body);
    }
}
