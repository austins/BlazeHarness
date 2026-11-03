using System.Text;

namespace BlazeHarness.Core.Web.Content.Office;

/// <summary>
/// Writes rows of cell text as a Markdown table.
/// </summary>
internal static class MarkdownTable
{
    /// <summary>
    /// Render rows as a table whose first row is the header.
    /// </summary>
    /// <param name="rows">The rows, each a list of cell texts. Short rows are padded.</param>
    /// <returns>The table, or an empty string when there is no cell.</returns>
    public static string Render(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var width = rows.Count == 0 ? 0 : rows.Max(row => row.Count);
        if (width == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < rows.Count; i++)
        {
            builder.Append('|');
            for (var column = 0; column < width; column++)
            {
                builder.Append(' ').Append(column < rows[i].Count ? Escape(rows[i][column]) : string.Empty).Append(" |");
            }

            builder.Append('\n');
            if (i == 0)
            {
                builder.Append('|').Append(string.Concat(Enumerable.Repeat(" --- |", width))).Append('\n');
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string Escape(string cell)
    {
        return cell.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
    }
}
