namespace BlazeHarness.Core.Git;

/// <summary>
/// Reads the output of <c>git diff --numstat -z --no-renames</c>.
/// </summary>
internal static class GitNumstatParser
{
    /// <summary>
    /// Parse the line counts of each file.
    /// </summary>
    /// <param name="output">The output, with records ending in a NUL character.</param>
    /// <returns>The counts by path; a binary file has null counts.</returns>
    public static Dictionary<string, (int? Added, int? Removed)> Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var counts = new Dictionary<string, (int? Added, int? Removed)>(StringComparer.Ordinal);
        foreach (var record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = record.Split('\t', 3);
            if (parts.Length != 3)
            {
                continue;
            }

            var added = int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var a) ? a : (int?)null;
            var removed = int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : (int?)null;
            counts[parts[2]] = (added, removed);
        }

        return counts;
    }
}
