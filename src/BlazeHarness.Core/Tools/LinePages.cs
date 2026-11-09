using System.Text;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// Pages long text by whole lines, so a tool can return it in parts that fit the output limit of a tool result.
/// </summary>
internal static class LinePages
{
    /// <summary>
    /// The text that ends a line too long to fit a page on its own.
    /// </summary>
    public const string CutLineMarker = " [rest of this line cut]";

    /// <summary>
    /// Append the lines from <paramref name="offset"/> that fit in <paramref name="room"/> characters.
    /// </summary>
    /// <remarks>A page always holds at least one line; a line too long for the page is cut and ends with <see cref="CutLineMarker"/>.</remarks>
    /// <param name="result">The page to append to.</param>
    /// <param name="lines">All the lines.</param>
    /// <param name="offset">The first line to append, starting at 1; at most the number of lines.</param>
    /// <param name="room">The characters left for lines on the page.</param>
    /// <returns>The number of the last line appended; the page is the last when it equals the number of lines.</returns>
    public static int Append(StringBuilder result, IReadOnlyList<string> lines, int offset, int room)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentOutOfRangeException.ThrowIfLessThan(offset, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, lines.Count);

        var next = offset - 1;
        while (next < lines.Count && (lines[next].Length + 1 <= room || next == offset - 1))
        {
            var line = lines[next].Length + 1 <= room ? lines[next] : Cut(lines[next], room - CutLineMarker.Length - 1) + CutLineMarker;
            result.Append(line).Append('\n');
            room -= line.Length + 1;
            next++;
        }

        return next;
    }

    private static string Cut(string line, int length)
    {
        length = Math.Clamp(length, 0, line.Length);
        if (length > 0 && char.IsHighSurrogate(line[length - 1]))
        {
            length--;
        }

        return line[..length];
    }
}
