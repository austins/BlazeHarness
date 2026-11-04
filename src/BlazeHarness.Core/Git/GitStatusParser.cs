namespace BlazeHarness.Core.Git;

/// <summary>
/// Reads the output of <c>git status --porcelain=v1 -z --no-renames</c>.
/// </summary>
internal static class GitStatusParser
{
    private const int PrefixLength = 3;

    /// <summary>
    /// Parse the entries of a status.
    /// </summary>
    /// <param name="output">The output, with entries ending in a NUL character.</param>
    /// <returns>The entries, without ignored files, in the order git gave them.</returns>
    public static IReadOnlyList<GitStatusEntry> Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var entries = new List<GitStatusEntry>();
        foreach (var record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (record.Length <= PrefixLength || record[2] != ' ' || (record[0] == '!' && record[1] == '!'))
            {
                continue;
            }

            entries.Add(new GitStatusEntry(record[PrefixLength..], record[0], record[1]));
        }

        return entries;
    }
}
