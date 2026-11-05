using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BlazeHarness.Core.Web.Content.Office;

/// <summary>
/// Reads the visible sheets of an Excel file as one Markdown table each. Cell values are read as stored; formulas show their last calculated value.
/// </summary>
internal static partial class SpreadsheetTextReader
{
    internal const int MaxRowsPerSheet = 2_000;
    internal const int MaxColumns = 100;

    /// <summary>
    /// The built-in number formats that show a date or a time.
    /// </summary>
    private static readonly HashSet<uint> DateFormatIds = [.. Enumerable.Range(14, 9).Select(id => (uint)id), .. Enumerable.Range(27, 10).Select(id => (uint)id), 45, 46, 47, .. Enumerable.Range(50, 9).Select(id => (uint)id)];

    /// <summary>
    /// Read the visible sheets of an Excel file into <paramref name="output"/>.
    /// </summary>
    /// <param name="document">The open file.</param>
    /// <param name="output">Where the Markdown goes.</param>
    /// <param name="cancellationToken">A token that stops the reading between rows.</param>
    /// <returns>A notice when rows or columns were left out, or null.</returns>
    public static string? Read(SpreadsheetDocument document, OfficeTextBuilder output, CancellationToken cancellationToken)
    {
        if (document.WorkbookPart is not { Workbook.Sheets: { } sheets } workbook)
        {
            return null;
        }

        var shared = SharedStrings(workbook);
        var dateStyles = DateStyles(workbook);
        var date1904 = workbook.Workbook.WorkbookProperties?.Date1904?.Value == true;
        var clipped = false;
        foreach (var sheet in sheets.Elements<Sheet>())
        {
            if (output.Full)
            {
                break;
            }

            if ((sheet.State?.Value ?? SheetStateValues.Visible) != SheetStateValues.Visible
                || sheet.Id?.Value is not { } id
                || workbook.GetPartById(id) is not WorksheetPart part)
            {
                continue;
            }

            var table = ReadSheet(part, shared, dateStyles, date1904, cancellationToken, out var sheetClipped);
            clipped |= sheetClipped;
            output.AddHeading(2, sheet.Name?.Value ?? "Sheet");
            output.AddBlock(MarkdownTable.Render(table));
        }

        return clipped ? $"Only the first {MaxRowsPerSheet} rows and {MaxColumns} columns of a sheet are read." : null;
    }

    private static List<IReadOnlyList<string>> ReadSheet(WorksheetPart part, string[] shared, HashSet<uint> dateStyles, bool date1904, CancellationToken cancellationToken, out bool clipped)
    {
        List<IReadOnlyList<string>> rows = [];
        clipped = false;
        foreach (var row in part.Worksheet?.GetFirstChild<SheetData>()?.Elements<Row>() ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cells = new List<string>();
            foreach (var cell in row.Elements<Cell>())
            {
                var column = ColumnIndex(cell.CellReference?.Value, cells.Count);
                if (column >= MaxColumns)
                {
                    clipped = true;
                    continue;
                }

                while (cells.Count < column)
                {
                    cells.Add(string.Empty);
                }

                cells.Add(CellText(cell, shared, dateStyles, date1904));
            }

            while (cells.Count > 0 && cells[^1].Length == 0)
            {
                cells.RemoveAt(cells.Count - 1);
            }

            if (cells.Count == 0)
            {
                continue;
            }

            if (rows.Count >= MaxRowsPerSheet)
            {
                clipped = true;
                break;
            }

            rows.Add(cells);
        }

        return rows;
    }

    private static string CellText(Cell cell, string[] shared, HashSet<uint> dateStyles, bool date1904)
    {
        var value = cell.CellValue?.Text;
        var type = cell.DataType?.Value;
        if (type == CellValues.SharedString)
        {
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < shared.Length ? shared[index] : string.Empty;
        }

        if (type == CellValues.InlineString)
        {
            return cell.InlineString?.InnerText ?? string.Empty;
        }

        if (type == CellValues.Boolean)
        {
            return value == "1" ? "TRUE" : "FALSE";
        }

        if (value is null)
        {
            return string.Empty;
        }

        if ((type is null || type == CellValues.Number) && cell.StyleIndex?.Value is { } style && dateStyles.Contains(style)
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial))
        {
            try
            {
                var date = DateTime.FromOADate(date1904 ? serial + 1462 : serial);
                return date.TimeOfDay == TimeSpan.Zero ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            catch (ArgumentException)
            {
                // A number outside the range of dates is shown as stored.
            }
        }

        return value;
    }

    /// <summary>
    /// Read a cell's column index, using the next column when its reference is omitted.
    /// </summary>
    /// <param name="reference">The cell reference, if present.</param>
    /// <param name="fallback">The column following the previous cell.</param>
    private static int ColumnIndex(string? reference, int fallback)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return fallback;
        }

        var index = 0;
        foreach (var character in reference)
        {
            if (character is < 'A' or > 'Z')
            {
                break;
            }

            index = (index * 26) + (character - 'A' + 1);
        }

        return index == 0 ? fallback : index - 1;
    }

    private static string[] SharedStrings(WorkbookPart workbook)
    {
        var items = workbook.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>() ?? [];
        return [.. items.Select(item => string.Concat(item.Descendants<Text>().Where(text => !text.Ancestors<PhoneticRun>().Any()).Select(text => text.Text)))];
    }

    private static HashSet<uint> DateStyles(WorkbookPart workbook)
    {
        var stylesheet = workbook.WorkbookStylesPart?.Stylesheet;
        var custom = (stylesheet?.NumberingFormats?.Elements<NumberingFormat>() ?? [])
            .Where(format => format.NumberFormatId?.Value is not null && IsDateFormat(format.FormatCode?.Value ?? string.Empty))
            .Select(format => format.NumberFormatId!.Value)
            .ToHashSet();
        HashSet<uint> styles = [];
        uint index = 0;
        foreach (var format in stylesheet?.CellFormats?.Elements<CellFormat>() ?? [])
        {
            if (format.NumberFormatId?.Value is { } id && (DateFormatIds.Contains(id) || custom.Contains(id)))
            {
                styles.Add(index);
            }

            index++;
        }

        return styles;
    }

    /// <summary>
    /// Determine whether a custom format contains a date or time letter outside quoted text and brackets.
    /// </summary>
    /// <param name="code">The custom number format.</param>
    private static bool IsDateFormat(string code)
    {
        return DatePart().IsMatch(QuotedOrBracketed().Replace(code, string.Empty));
    }

    [GeneratedRegex("\"[^\"]*\"|\\[[^\\]]*\\]|\\\\.", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex QuotedOrBracketed();

    [GeneratedRegex("[dmyhs]", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DatePart();
}
