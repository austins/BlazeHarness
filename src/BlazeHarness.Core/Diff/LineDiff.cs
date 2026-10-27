namespace BlazeHarness.Core.Diff;

/// <summary>
/// Compares two texts line by line.
/// </summary>
public static class LineDiff
{
    /// <summary>
    /// The most cells the comparison table may have. Larger edits are shown as all removed, then all added.
    /// </summary>
    private const long MaxCells = 4_000_000;

    /// <summary>
    /// Compare two texts line by line.
    /// </summary>
    /// <remarks>
    /// Lines that both texts share at the start and the end are kept as they are, and the lines between are matched with
    /// the longest common subsequence. Line endings are ignored, so a CRLF text compares equal to the same LF text.
    /// </remarks>
    /// <param name="oldText">The text before the change.</param>
    /// <param name="newText">The text after the change.</param>
    /// <returns>The lines in order, each marked as shared, removed or added.</returns>
    public static IReadOnlyList<DiffLine> Compare(string oldText, string newText)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        ArgumentNullException.ThrowIfNull(newText);

        var before = Split(oldText);
        var after = Split(newText);

        var start = 0;
        while (start < before.Length && start < after.Length && before[start] == after[start])
        {
            start++;
        }

        var endBefore = before.Length;
        var endAfter = after.Length;
        while (endBefore > start && endAfter > start && before[endBefore - 1] == after[endAfter - 1])
        {
            endBefore--;
            endAfter--;
        }

        var lines = new List<DiffLine>(before.Length + after.Length);
        for (var i = 0; i < start; i++)
        {
            lines.Add(new DiffLine(DiffKind.Same, before[i]));
        }

        Middle(before.AsSpan(start, endBefore - start), after.AsSpan(start, endAfter - start), lines);

        for (var i = endBefore; i < before.Length; i++)
        {
            lines.Add(new DiffLine(DiffKind.Same, before[i]));
        }

        return lines;
    }

    private static string[] Split(string text) =>
        text.Length == 0 ? [] : text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    private static void Middle(ReadOnlySpan<string> before, ReadOnlySpan<string> after, List<DiffLine> lines)
    {
        if ((long)before.Length * after.Length > MaxCells)
        {
            AddAll(lines, DiffKind.Removed, before);
            AddAll(lines, DiffKind.Added, after);
            return;
        }

        // common[i * width + j] is the length of the longest common subsequence of before[i..] and after[j..].
        var width = after.Length + 1;
        var common = new int[(before.Length + 1) * width];
        for (var i = before.Length - 1; i >= 0; i--)
        {
            for (var j = after.Length - 1; j >= 0; j--)
            {
                common[(i * width) + j] = before[i] == after[j]
                    ? common[((i + 1) * width) + j + 1] + 1
                    : Math.Max(common[((i + 1) * width) + j], common[(i * width) + j + 1]);
            }
        }

        var a = 0;
        var b = 0;
        while (a < before.Length && b < after.Length)
        {
            if (before[a] == after[b])
            {
                lines.Add(new DiffLine(DiffKind.Same, before[a]));
                a++;
                b++;
            }
            else if (common[((a + 1) * width) + b] >= common[(a * width) + b + 1])
            {
                lines.Add(new DiffLine(DiffKind.Removed, before[a++]));
            }
            else
            {
                lines.Add(new DiffLine(DiffKind.Added, after[b++]));
            }
        }

        AddAll(lines, DiffKind.Removed, before[a..]);
        AddAll(lines, DiffKind.Added, after[b..]);
    }

    private static void AddAll(List<DiffLine> lines, DiffKind kind, ReadOnlySpan<string> texts)
    {
        foreach (var text in texts)
        {
            lines.Add(new DiffLine(kind, text));
        }
    }
}
