using BlazeHarness.Core.Diff;
using System.Globalization;

namespace BlazeHarness.Core.Git;

/// <summary>
/// Reads the unified diff of one file, as <c>git diff</c> writes it.
/// </summary>
internal static class GitDiffParser
{
    /// <summary>
    /// Gets how many lines of a diff are kept; more are cut off.
    /// </summary>
    public const int MaxLines = 4000;

    /// <summary>
    /// Parse a diff.
    /// </summary>
    /// <param name="path">The path of the file, relative to the repository root.</param>
    /// <param name="output">The diff text.</param>
    /// <param name="outputWasCut">Whether the text was already cut short because it was too large.</param>
    /// <returns>The file's diff.</returns>
    public static GitFileDiff Parse(string path, string output, bool outputWasCut)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(output);
        var hunks = new List<GitDiffHunk>();
        List<GitDiffLine>? lines = null;
        string header = string.Empty;
        var oldNumber = 0;
        var newNumber = 0;
        var total = 0;
        var binary = false;
        var truncated = outputWasCut;

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.EndsWith('\r') ? raw[..^1] : raw;
            if (line.StartsWith("@@ ", StringComparison.Ordinal))
            {
                if (lines is not null)
                {
                    hunks.Add(new GitDiffHunk(header, lines));
                }

                if (!TryParseHeader(line, out oldNumber, out newNumber))
                {
                    lines = null;
                    continue;
                }

                header = line;
                lines = [];
                continue;
            }

            if (lines is null)
            {
                binary |= line.StartsWith("Binary files ", StringComparison.Ordinal) || line.StartsWith("GIT binary patch", StringComparison.Ordinal);
                continue;
            }

            if (line.Length == 0 || line[0] == '\\')
            {
                continue;
            }

            if (total >= MaxLines)
            {
                truncated = true;
                break;
            }

            var text = line[1..];
            switch (line[0])
            {
                case '+':
                    lines.Add(new GitDiffLine(DiffKind.Added, null, newNumber++, text));
                    break;
                case '-':
                    lines.Add(new GitDiffLine(DiffKind.Removed, oldNumber++, null, text));
                    break;
                default:
                    lines.Add(new GitDiffLine(DiffKind.Same, oldNumber++, newNumber++, text));
                    break;
            }

            total++;
        }

        if (lines is not null)
        {
            hunks.Add(new GitDiffHunk(header, lines));
        }

        return new GitFileDiff(path, binary, truncated, hunks);
    }

    /// <summary>
    /// Parse the starting line numbers from a unified diff hunk header.
    /// </summary>
    /// <remarks>A header reads "@@ -12,5 +12,6 @@ optional text"; counts are omitted when they are one.</remarks>
    /// <param name="line">The hunk header.</param>
    /// <param name="oldStart">The starting line in the original file.</param>
    /// <param name="newStart">The starting line in the changed file.</param>
    private static bool TryParseHeader(string line, out int oldStart, out int newStart)
    {
        oldStart = 0;
        newStart = 0;
        var end = line.IndexOf(" @@", 3, StringComparison.Ordinal);
        if (end < 0)
        {
            return false;
        }

        var ranges = line[3..end].Split(' ');
        return ranges.Length == 2
            && ranges[0].StartsWith('-')
            && ranges[1].StartsWith('+')
            && TryStart(ranges[0], out oldStart)
            && TryStart(ranges[1], out newStart);
    }

    private static bool TryStart(string range, out int start)
    {
        var comma = range.IndexOf(',', StringComparison.Ordinal);
        var digits = comma < 0 ? range[1..] : range[1..comma];
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out start);
    }
}
