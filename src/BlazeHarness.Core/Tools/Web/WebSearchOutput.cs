using System.Globalization;
using System.Text;

namespace BlazeHarness.Core.Tools.Web;

/// <summary>
/// Writes the results of a <c>web_search</c> call for the model, and reads them back for display.
/// </summary>
/// <remarks>
/// Each result is a numbered title line, then the address and an optional snippet, each indented by three spaces.
/// Titles and snippets are written on one line, so the reader can tell the parts apart.
/// </remarks>
public static class WebSearchOutput
{
    /// <summary>
    /// The name of the tool whose output this is.
    /// </summary>
    public const string ToolName = "web_search";

    private const string Indent = "   ";

    /// <summary>
    /// Read the results back from the output of a call.
    /// </summary>
    /// <param name="output">The output of the call.</param>
    /// <returns>The results in order, or an empty list when the output has none or one has an address that is not http or https.</returns>
    public static IReadOnlyList<WebSearchHit> Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var hits = new List<WebSearchHit>();
        var lines = output.Split('\n');
        var i = 0;
        while (i + 1 < lines.Length)
        {
            if (TitleOf(lines[i], hits.Count + 1) is not { } title || !lines[i + 1].StartsWith(Indent, StringComparison.Ordinal))
            {
                i++;
                continue;
            }

            if (!Uri.TryCreate(lines[i + 1][Indent.Length..], UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
            {
                return [];
            }

            var snippet = i + 2 < lines.Length && lines[i + 2].StartsWith(Indent, StringComparison.Ordinal) ? lines[i + 2][Indent.Length..] : string.Empty;
            hits.Add(new WebSearchHit(title, url, snippet));
            i += snippet.Length > 0 ? 3 : 2;
        }

        return hits;
    }

    /// <summary>
    /// Append one result.
    /// </summary>
    /// <param name="builder">The output so far.</param>
    /// <param name="number">The result's number, starting at 1.</param>
    /// <param name="title">The title of the page.</param>
    /// <param name="url">The address of the page.</param>
    /// <param name="snippet">A short extract of the page, or an empty string.</param>
    internal static void AppendResult(StringBuilder builder, int number, string title, string url, string snippet)
    {
        builder.Append(CultureInfo.InvariantCulture, $"\n{number}. {OneLine(title)}\n{Indent}{OneLine(url)}\n");
        var shortText = OneLine(snippet);
        if (shortText.Length > 0)
        {
            builder.Append(Indent).Append(shortText).Append('\n');
        }
    }

    private static string? TitleOf(string line, int number)
    {
        var prefix = string.Create(CultureInfo.InvariantCulture, $"{number}. ");
        return line.StartsWith(prefix, StringComparison.Ordinal) ? line[prefix.Length..] : null;
    }

    private static string OneLine(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
