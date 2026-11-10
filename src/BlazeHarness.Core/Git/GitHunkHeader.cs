using System.Globalization;

namespace BlazeHarness.Core.Git;

/// <summary>
/// The line ranges of a unified diff hunk header, such as <c>@@ -12,5 +12,6 @@ optional text</c>.
/// </summary>
/// <param name="OldStart">The first line of the range in the original file.</param>
/// <param name="OldCount">How many lines the range covers in the original file.</param>
/// <param name="NewStart">The first line of the range in the changed file; when <paramref name="NewCount"/> is zero, the line the range follows.</param>
/// <param name="NewCount">How many lines the range covers in the changed file.</param>
internal readonly record struct GitHunkHeader(int OldStart, int OldCount, int NewStart, int NewCount)
{
    /// <summary>
    /// Parse a hunk header.
    /// </summary>
    /// <remarks>Counts are omitted when they are one.</remarks>
    /// <param name="line">The hunk header line.</param>
    /// <param name="header">The ranges, when the line is a valid header.</param>
    /// <returns>Whether the line is a valid hunk header.</returns>
    public static bool TryParse(string line, out GitHunkHeader header)
    {
        header = default;
        if (!line.StartsWith("@@ ", StringComparison.Ordinal))
        {
            return false;
        }

        var end = line.IndexOf(" @@", 3, StringComparison.Ordinal);
        if (end < 0)
        {
            return false;
        }

        var ranges = line[3..end].Split(' ');
        if (ranges.Length != 2
            || !ranges[0].StartsWith('-')
            || !ranges[1].StartsWith('+')
            || !TryRange(ranges[0], out var oldStart, out var oldCount)
            || !TryRange(ranges[1], out var newStart, out var newCount))
        {
            return false;
        }

        header = new GitHunkHeader(oldStart, oldCount, newStart, newCount);
        return true;
    }

    private static bool TryRange(string range, out int start, out int count)
    {
        count = 1;
        var comma = range.IndexOf(',', StringComparison.Ordinal);
        if (comma < 0)
        {
            return int.TryParse(range[1..], NumberStyles.None, CultureInfo.InvariantCulture, out start);
        }

        return int.TryParse(range[1..comma], NumberStyles.None, CultureInfo.InvariantCulture, out start)
            && int.TryParse(range[(comma + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out count);
    }
}
