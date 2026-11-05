using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Globalization;
using System.Text;

namespace BlazeHarness.Core.Web.Content.Office;

/// <summary>
/// Reads the body text of a Word file: headings, paragraphs, list items and tables. Headers, footers, notes and comments are left out.
/// </summary>
internal static class WordTextReader
{
    /// <summary>
    /// Read the body of a Word file into <paramref name="output"/>.
    /// </summary>
    /// <param name="document">The open file.</param>
    /// <param name="output">Where the Markdown goes.</param>
    /// <param name="cancellationToken">A token that stops the reading between blocks.</param>
    public static void Read(WordprocessingDocument document, OfficeTextBuilder output, CancellationToken cancellationToken)
    {
        var main = document.MainDocumentPart;
        if (main?.Document?.Body is not { } body)
        {
            return;
        }

        ReadBlocks(body.Elements(), StyleNames(main), output, cancellationToken);
    }

    private static void ReadBlocks(IEnumerable<OpenXmlElement> blocks, Dictionary<string, string> styles, OfficeTextBuilder output, CancellationToken cancellationToken)
    {
        foreach (var block in blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (output.Full)
            {
                return;
            }

            switch (block)
            {
                case Paragraph paragraph:
                    ReadParagraph(paragraph, styles, output);
                    break;
                case Table table:
                    output.AddBlock(MarkdownTable.Render([.. table.Elements<TableRow>().Select(row => (IReadOnlyList<string>)[.. row.Elements<TableCell>().Select(CellText)])]));
                    break;
                case SdtBlock { SdtContentBlock: { } content }:
                    ReadBlocks(content.Elements(), styles, output, cancellationToken);
                    break;
                default:
                    break;
            }
        }
    }

    private static void ReadParagraph(Paragraph paragraph, Dictionary<string, string> styles, OfficeTextBuilder output)
    {
        var text = TextOf(paragraph).Trim();
        if (text.Length == 0)
        {
            return;
        }

        if (HeadingLevel(paragraph, styles) is { } level)
        {
            output.AddHeading(level, text);
        }
        else if (paragraph.ParagraphProperties?.NumberingProperties is { } numbering)
        {
            var indent = new string(' ', 2 * Math.Clamp(numbering.NumberingLevelReference?.Val?.Value ?? 0, 0, 8));
            output.AddListItem($"{indent}- {text}");
        }
        else
        {
            output.AddBlock(text);
        }
    }

    private static string CellText(TableCell cell)
    {
        return string.Join(' ', cell.Descendants<Paragraph>().Select(paragraph => TextOf(paragraph).Trim()).Where(text => text.Length > 0));
    }

    private static string TextOf(OpenXmlElement element)
    {
        var builder = new StringBuilder();
        Append(element, builder);
        return builder.ToString();
    }

    private static void Append(OpenXmlElement element, StringBuilder builder)
    {
        foreach (var child in element.Elements())
        {
            switch (child)
            {
                case Text text:
                    builder.Append(text.Text);
                    break;
                case TabChar:
                    builder.Append('\t');
                    break;
                case Break:
                    builder.Append(' ');
                    break;
                case NoBreakHyphen:
                    builder.Append('-');
                    break;
                case AlternateContentFallback:
                    // The fallback repeats what the choice next to it already holds.
                    break;
                default:
                    Append(child, builder);
                    break;
            }
        }
    }

    /// <summary>
    /// Find a paragraph's heading level from its style name, then its outline level.
    /// </summary>
    /// <remarks>Style IDs can be localized or custom.</remarks>
    /// <param name="paragraph">The paragraph to inspect.</param>
    /// <param name="styles">The style names indexed by style ID.</param>
    private static int? HeadingLevel(Paragraph paragraph, Dictionary<string, string> styles)
    {
        var properties = paragraph.ParagraphProperties;
        if (properties?.ParagraphStyleId?.Val?.Value is { } id)
        {
            var name = (styles.TryGetValue(id, out var styleName) ? styleName : id).Replace(" ", string.Empty, StringComparison.Ordinal);
            if (name.Equals("Title", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (name.StartsWith("heading", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(name.AsSpan("heading".Length), NumberStyles.None, CultureInfo.InvariantCulture, out var level))
            {
                return level;
            }
        }

        return properties?.OutlineLevel?.Val?.Value is { } outline and < 9 ? outline + 1 : null;
    }

    private static Dictionary<string, string> StyleNames(MainDocumentPart main)
    {
        Dictionary<string, string> names = new(StringComparer.Ordinal);
        foreach (var style in main.StyleDefinitionsPart?.Styles?.Elements<Style>() ?? [])
        {
            if (style.StyleId?.Value is { } id && style.StyleName?.Val?.Value is { } name)
            {
                names[id] = name;
            }
        }

        return names;
    }
}
