using BlazeHarness.Core.Diff;
using System.Text;

namespace BlazeHarness.Web.Services;

/// <summary>
/// Changes the HTML of one colored line of code.
/// </summary>
internal static class DiffHtml
{
    /// <summary>
    /// Get the text of a line of colored HTML.
    /// </summary>
    /// <param name="html">HTML made of tags and the entities <c>WebUtility.HtmlEncode</c> writes.</param>
    /// <returns>The text without tags, with the entities decoded.</returns>
    public static string TextOf(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return System.Net.WebUtility.HtmlDecode(StripTags(html));
    }

    /// <summary>
    /// Wrap parts of the text of colored HTML in <c>mark</c> elements.
    /// </summary>
    /// <remarks>
    /// A mark that crosses a tag is closed before the tag and opened again after it, so the result stays well-formed.
    /// </remarks>
    /// <param name="html">HTML made of tags and the entities <c>WebUtility.HtmlEncode</c> writes.</param>
    /// <param name="ranges">The parts to mark, as offsets in the text without tags, in ascending order without overlap.</param>
    /// <param name="cssClass">The class of the marks.</param>
    /// <returns>The HTML with the marks.</returns>
    public static string Mark(string html, IReadOnlyList<TextRange> ranges, string cssClass)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(ranges);
        if (ranges.Count == 0)
        {
            return html;
        }

        var open = $"<mark class=\"{cssClass}\">";
        var result = new StringBuilder(html.Length + (ranges.Count * (open.Length + 7)));
        var next = 0;
        var marking = false;
        var position = 0;
        var i = 0;
        while (i < html.Length)
        {
            if (html[i] == '<')
            {
                var end = html.IndexOf('>', i, StringComparison.Ordinal);
                if (marking)
                {
                    result.Append("</mark>");
                }

                result.Append(html, i, end + 1 - i);
                if (marking)
                {
                    result.Append(open);
                }

                i = end + 1;
                continue;
            }

            if (marking && position == ranges[next].Start + ranges[next].Length)
            {
                result.Append("</mark>");
                marking = false;
                next++;
            }

            if (!marking && next < ranges.Count && position == ranges[next].Start)
            {
                result.Append(open);
                marking = true;
            }

            // An entity such as &amp; is one character of text, or two when it stands for a character outside the basic plane.
            var length = html[i] == '&' && html.IndexOf(';', i, StringComparison.Ordinal) is var semicolon and > 0 ? semicolon + 1 - i : 1;
            result.Append(html, i, length);
            position += length > 1 && int.TryParse(html.AsSpan(i + 2, length - 3), out var code) && html[i + 1] == '#' && code > 0xFFFF ? 2 : 1;
            i += length;
        }

        if (marking)
        {
            result.Append("</mark>");
        }

        return result.ToString();
    }

    private static string StripTags(string html)
    {
        var text = new StringBuilder(html.Length);
        var i = 0;
        while (i < html.Length)
        {
            if (html[i] == '<')
            {
                i = html.IndexOf('>', i, StringComparison.Ordinal) + 1;
            }
            else
            {
                text.Append(html[i++]);
            }
        }

        return text.ToString();
    }
}
