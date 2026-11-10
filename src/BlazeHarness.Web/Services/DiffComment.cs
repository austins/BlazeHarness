using BlazeHarness.Core.Diff;
using BlazeHarness.Core.Git;
using System.Globalization;
using System.Text;

namespace BlazeHarness.Web.Services;

/// <summary>
/// A remark the user wrote about one or more lines of one block of a changed file, to send to the assistant.
/// </summary>
/// <param name="Path">The path of the file, relative to the folder.</param>
/// <param name="Lines">The lines the remark is about, in diff order; at least one.</param>
/// <param name="Text">What the user wrote.</param>
public sealed record DiffComment(string Path, IReadOnlyList<GitDiffLine> Lines, string Text)
{
    /// <summary>
    /// Gets a value indicating whether every line was removed, so the line numbers are those of the old version.
    /// </summary>
    public bool IsRemoved => Lines.All(l => l.Kind == DiffKind.Removed);

    /// <summary>
    /// Gets a value indicating whether the lines mix added, removed or unchanged lines.
    /// </summary>
    public bool IsMixed => Lines.Select(l => l.Kind).Distinct().Skip(1).Any();

    /// <summary>
    /// Gets the first line number of the range, in the old version when <see cref="IsRemoved"/> and in the new version otherwise.
    /// </summary>
    public int First => Numbers.First();

    /// <summary>
    /// Gets the last line number of the range, numbered like <see cref="First"/>.
    /// </summary>
    public int End => Numbers.Last();

    /// <summary>The most lines a remark quotes; the line numbers point the assistant to the rest.</summary>
    internal const int MaxQuotedLines = 3;

    /// <summary>
    /// Write the remark as text for the message to the assistant: where it is, the lines it is about and what the user wrote.
    /// </summary>
    /// <returns>The text, in Markdown.</returns>
    public string ToMessage()
    {
        var side = IsRemoved ? "removed line" : "line";
        var where = First == End ? $"{side} {First}" : $"{side}s {First}–{End}";
        var text = new StringBuilder().Append(CultureInfo.InvariantCulture, $"`{Path}`, {where}:");
        foreach (var line in Lines.Take(MaxQuotedLines))
        {
            var sign = line.Kind switch { DiffKind.Added => "+ ", DiffKind.Removed => "- ", _ => "  " };
            text.Append(CultureInfo.InvariantCulture, $"\n> {(IsMixed ? sign : string.Empty)}{line.Text}");
        }

        if (Lines.Count > MaxQuotedLines)
        {
            text.Append("\n> …");
        }

        // The blank line ends the quote; Markdown would otherwise fold the next line into it.
        return text.Append("\n\n").Append(Text).ToString();
    }

    private IEnumerable<int> Numbers => IsRemoved
        ? Lines.Select(l => l.OldNumber ?? 0)
        : Lines.Where(l => l.Kind != DiffKind.Removed).Select(l => l.NewNumber ?? 0);
}
