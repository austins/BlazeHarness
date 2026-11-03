#pragma warning disable S3220 // The SDK's element constructors take params children; the IEnumerable overload it reports is not the intended call.
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using System.Globalization;
using System.IO.Compression;
using D = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace BlazeHarness.Tests.Shared.Office;

/// <summary>
/// Builds small docx, xlsx and pptx files in memory for tests.
/// </summary>
public static class OfficeFiles
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

    public static W.Paragraph RawParagraph(params OpenXmlElement[] children)
    {
        return new W.Paragraph(children);
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
        return new S.Cell { CellReference = reference, DataType = S.CellValues.SharedString, CellValue = new S.CellValue(index.ToString(CultureInfo.InvariantCulture)) };
    }

    public static S.Cell NumberCell(string reference, string value, uint? style = null)
    {
        return new S.Cell { CellReference = reference, CellValue = new S.CellValue(value), StyleIndex = style };
    }

    public static S.Cell FormulaCell(string reference, string formula, string cachedValue)
    {
        return new S.Cell { CellReference = reference, CellFormula = new S.CellFormula(formula), CellValue = new S.CellValue(cachedValue) };
    }

    public static S.Cell BooleanCell(string reference, bool value)
    {
        return new S.Cell { CellReference = reference, DataType = S.CellValues.Boolean, CellValue = new S.CellValue(value ? "1" : "0") };
    }

    public static S.Row Row(params S.Cell[] cells)
    {
        return new S.Row(cells);
    }

    public static byte[] Spreadsheet(
        IEnumerable<(string Name, bool Hidden, S.Row[] Rows)> sheets,
        string[]? sharedStrings = null,
        S.Stylesheet? styles = null,
        S.SharedStringItem[]? sharedItems = null,
        bool withChartSheet = false,
        string? title = null)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new S.Workbook(new S.Sheets());
            if (sharedItems is not null || sharedStrings is not null)
            {
                var items = sharedItems ?? [.. sharedStrings!.Select(text => new S.SharedStringItem(new S.Text(text)))];
                workbook.AddNewPart<SharedStringTablePart>().SharedStringTable = new S.SharedStringTable(items);
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

            if (withChartSheet)
            {
                var chart = workbook.AddNewPart<ChartsheetPart>();
                chart.Chartsheet = new S.Chartsheet();
                workbook.Workbook.Sheets!.Append(new S.Sheet { Id = workbook.GetIdOfPart(chart), SheetId = id, Name = "ChartOnly" });
            }

            if (title is not null)
            {
                document.PackageProperties.Title = title;
            }
        }

        return stream.ToArray();
    }

    public static byte[] Presentation(IEnumerable<SlideSpec> slides, string? title = null)
    {
        using var stream = new MemoryStream();
        using (var document = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            var presentation = document.AddPresentationPart();
            presentation.Presentation = new P.Presentation(new P.SlideIdList());
            uint id = 256;
            foreach (var spec in slides)
            {
                var slide = presentation.AddNewPart<SlidePart>();
                var tree = new P.ShapeTree();
                if (spec.Title is not null)
                {
                    tree.Append(TextShape([spec.Title], P.PlaceholderValues.Title));
                }

                if (spec.Lines is { Count: > 0 } lines)
                {
                    var shape = TextShape([.. lines], null);
                    tree.Append(spec.Grouped ? GroupOf(shape) : shape);
                }

                if (spec.Table is not null)
                {
                    tree.Append(TableFrame(spec.Table));
                }

                slide.Slide = new P.Slide(new P.CommonSlideData(tree));
                if (spec.Notes is not null)
                {
                    var notesPart = slide.AddNewPart<NotesSlidePart>();
                    notesPart.NotesSlide = new P.NotesSlide(new P.CommonSlideData(new P.ShapeTree(TextShape([spec.Notes], P.PlaceholderValues.Body))));
                }

                presentation.Presentation.SlideIdList!.Append(new P.SlideId { Id = id++, RelationshipId = presentation.GetIdOfPart(slide) });
            }

            if (title is not null)
            {
                document.PackageProperties.Title = title;
            }
        }

        return stream.ToArray();
    }

    public static byte[] ZipWithEntry(string name)
    {
        return Zip(archive => WriteEntry(archive, name, "x"u8.ToArray()));
    }

    /// <summary>
    /// Replaces the content of one entry in a zip with bytes that are not valid XML.
    /// </summary>
    /// <param name="file">The zip to change.</param>
    /// <param name="name">The entry to overwrite.</param>
    public static byte[] WithBrokenEntry(byte[] file, string name)
    {
        using var stream = new MemoryStream();
        stream.Write(file);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry(name)!.Delete();
            WriteEntry(archive, name, "<w:document><oops"u8.ToArray());
        }

        return stream.ToArray();
    }

    public static byte[] ZipWithEntries(int count)
    {
        return Zip(archive =>
        {
            for (var i = 0; i < count; i++)
            {
                WriteEntry(archive, string.Create(CultureInfo.InvariantCulture, $"e{i}.txt"), [1]);
            }
        });
    }

    /// <summary>
    /// Builds a zip whose one entry declares <paramref name="length"/> bytes when unpacked but stays small, since the bytes are all zero.
    /// </summary>
    /// <param name="length">The unpacked size of the entry in bytes.</param>
    public static byte[] ZipWithLargeEntry(long length)
    {
        return Zip(archive =>
        {
            using var entry = archive.CreateEntry("big.bin", CompressionLevel.Fastest).Open();
            var chunk = new byte[1024 * 1024];
            for (long written = 0; written < length; written += chunk.Length)
            {
                entry.Write(chunk, 0, (int)Math.Min(chunk.Length, length - written));
            }
        });
    }

    private static byte[] Zip(Action<ZipArchive> fill)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            fill(archive);
        }

        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] content)
    {
        using var entry = archive.CreateEntry(name).Open();
        entry.Write(content, 0, content.Length);
    }

    private static P.GroupShape GroupOf(P.Shape shape)
    {
        return new P.GroupShape(
            new P.NonVisualGroupShapeProperties(new P.NonVisualDrawingProperties { Id = 2, Name = "Group" }, new P.NonVisualGroupShapeDrawingProperties(), new P.ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties(),
            shape);
    }

    private static P.GraphicFrame TableFrame(IReadOnlyList<IReadOnlyList<string>> rows)
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
