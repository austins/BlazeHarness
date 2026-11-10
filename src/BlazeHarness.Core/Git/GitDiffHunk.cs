using BlazeHarness.Core.Diff;
using System.Globalization;

namespace BlazeHarness.Core.Git;

/// <summary>
/// A run of changed lines with the unchanged lines around them.
/// </summary>
/// <param name="Header">The <c>@@ -1,3 +1,4 @@</c> line that starts the hunk.</param>
/// <param name="Lines">The lines of the hunk.</param>
public sealed record GitDiffHunk(string Header, IReadOnlyList<GitDiffLine> Lines)
{
    /// <summary>
    /// Split the hunk into its separate changes: each run of added and removed lines, as a hunk of its own without context.
    /// </summary>
    /// <remarks>Each part can be reverted on its own with <see cref="GitChangeReverter.RevertHunkAsync"/>.</remarks>
    /// <returns>The changes in order, with the index in <see cref="Lines"/> each starts at; empty when the header can't be read.</returns>
    public IReadOnlyList<(int Start, GitDiffHunk Change)> SplitChanges()
    {
        if (!GitHunkHeader.TryParse(Header, out var header))
        {
            return [];
        }

        List<(int, GitDiffHunk)> changes = [];

        // The last line passed on each side; a header with no lines on a side already names the line its range follows.
        var oldLine = header.OldCount == 0 ? header.OldStart : header.OldStart - 1;
        var newLine = header.NewCount == 0 ? header.NewStart : header.NewStart - 1;
        var i = 0;
        while (i < Lines.Count)
        {
            if (Lines[i].Kind == DiffKind.Same)
            {
                oldLine++;
                newLine++;
                i++;
                continue;
            }

            var start = i;
            while (i < Lines.Count && Lines[i].Kind != DiffKind.Same)
            {
                i++;
            }

            var run = Lines.Skip(start).Take(i - start).ToList();
            var removed = run.Count(l => l.Kind == DiffKind.Removed);
            var added = run.Count - removed;
            var text = string.Create(
                CultureInfo.InvariantCulture,
                $"@@ -{(removed == 0 ? oldLine : oldLine + 1)},{removed} +{(added == 0 ? newLine : newLine + 1)},{added} @@");
            changes.Add((start, new GitDiffHunk(text, run)));
            oldLine += removed;
            newLine += added;
        }

        return changes;
    }
}
