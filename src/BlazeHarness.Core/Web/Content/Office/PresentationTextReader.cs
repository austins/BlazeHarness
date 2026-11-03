using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using System.Globalization;
using System.Text;
using Drawing = DocumentFormat.OpenXml.Drawing;

namespace BlazeHarness.Core.Web.Content.Office;

/// <summary>
/// Reads the text of a PowerPoint file, one section per slide: the title, the text of each shape, tables and the speaker notes.
/// </summary>
internal static class PresentationTextReader
{
    /// <summary>
    /// Read the slides of a PowerPoint file into <paramref name="output"/>.
    /// </summary>
    /// <param name="document">The open file.</param>
    /// <param name="output">Where the Markdown goes.</param>
    /// <param name="cancellationToken">A token that stops the reading between slides.</param>
    public static void Read(PresentationDocument document, OfficeTextBuilder output, CancellationToken cancellationToken)
    {
        if (document.PresentationPart is not { Presentation.SlideIdList: { } slideIds } presentation)
        {
            return;
        }

        var number = 0;
        foreach (var slideId in slideIds.Elements<SlideId>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (output.Full)
            {
                return;
            }

            number++;
            if (slideId.RelationshipId?.Value is not { } id || presentation.GetPartById(id) is not SlidePart slide)
            {
                continue;
            }

            var title = SlideTitle(slide);
            output.AddHeading(2, title.Length > 0 ? string.Create(CultureInfo.InvariantCulture, $"Slide {number}: {title}") : string.Create(CultureInfo.InvariantCulture, $"Slide {number}"));
            ReadShapes(slide.Slide?.CommonSlideData?.ShapeTree?.Elements() ?? [], output, includeTitle: false);
            if (SlideNotes(slide) is { Length: > 0 } notes)
            {
                output.AddBlock($"Notes: {notes}");
            }
        }
    }

    private static void ReadShapes(IEnumerable<OpenXmlElement> shapes, OfficeTextBuilder output, bool includeTitle)
    {
        foreach (var shape in shapes)
        {
            switch (shape)
            {
                case Shape { TextBody: { } body } text when includeTitle || !IsTitle(text):
                    foreach (var paragraph in body.Elements<Drawing.Paragraph>())
                    {
                        if (TextOf(paragraph).Trim() is { Length: > 0 } line)
                        {
                            output.AddListItem($"- {line}");
                        }
                    }

                    break;
                case GroupShape group:
                    ReadShapes(group.Elements(), output, includeTitle);
                    break;
                case GraphicFrame frame:
                    foreach (var table in frame.Descendants<Drawing.Table>())
                    {
                        output.AddBlock(MarkdownTable.Render([.. table.Elements<Drawing.TableRow>().Select(row => (IReadOnlyList<string>)[.. row.Elements<Drawing.TableCell>().Select(cell => TextOf(cell).Trim())])]));
                    }

                    break;
                default:
                    break;
            }
        }
    }

    private static string SlideTitle(SlidePart slide)
    {
        var title = (slide.Slide?.CommonSlideData?.ShapeTree?.Descendants<Shape>() ?? []).FirstOrDefault(IsTitle);
        return title?.TextBody is { } body ? string.Join(' ', body.Elements<Drawing.Paragraph>().Select(paragraph => TextOf(paragraph).Trim()).Where(text => text.Length > 0)) : string.Empty;
    }

    private static string SlideNotes(SlidePart slide)
    {
        var shapes = slide.NotesSlidePart?.NotesSlide?.CommonSlideData?.ShapeTree?.Descendants<Shape>() ?? [];
        var notes = shapes
            .Where(shape => shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.GetFirstChild<PlaceholderShape>()?.Type?.Value == PlaceholderValues.Body)
            .SelectMany(shape => shape.TextBody?.Elements<Drawing.Paragraph>() ?? [])
            .Select(paragraph => TextOf(paragraph).Trim())
            .Where(text => text.Length > 0);
        return string.Join(' ', notes);
    }

    private static bool IsTitle(Shape shape)
    {
        var type = shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.GetFirstChild<PlaceholderShape>()?.Type?.Value;
        return type == PlaceholderValues.Title || type == PlaceholderValues.CenteredTitle;
    }

    private static string TextOf(OpenXmlElement element)
    {
        var builder = new StringBuilder();
        foreach (var node in element.Descendants())
        {
            switch (node)
            {
                case Drawing.Text text:
                    builder.Append(text.Text);
                    break;
                case Drawing.Break:
                    builder.Append(' ');
                    break;
                default:
                    break;
            }
        }

        return builder.ToString();
    }
}
