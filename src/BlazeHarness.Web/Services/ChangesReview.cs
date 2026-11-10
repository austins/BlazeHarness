using BlazeHarness.Core.Diff;
using BlazeHarness.Core.Git;

namespace BlazeHarness.Web.Services;

/// <summary>
/// What the user has done while reading the changes of a chat: which files they looked at or marked as viewed, the remark they are writing, and how the list and diff are arranged.
/// </summary>
public sealed class ChangesReview
{
    /// <summary>The number of lines around a change the diff starts with.</summary>
    public const int DefaultContext = 3;

    /// <summary>The number of lines a click on "show more" adds around each change.</summary>
    public const int ContextStep = 20;

    private readonly Dictionary<string, string> _viewed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _looked = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the folders of the file list the user collapsed.
    /// </summary>
    public HashSet<string> CollapsedFolders { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the text the file list is filtered by.
    /// </summary>
    public string Filter { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the path of the file the user last opened, so the panel opens on it again.
    /// </summary>
    public string? SelectedPath { get; set; }

    /// <summary>
    /// Gets or sets the remark the user is still writing, kept with its untrimmed text so the form can be filled in again.
    /// </summary>
    public DiffComment? Draft { get; set; }

    /// <summary>
    /// Gets or sets the number of lines of context around each change.
    /// </summary>
    public int Context { get; set; } = DefaultContext;

    /// <summary>
    /// Gets or sets a value indicating whether the diff shows the whole file.
    /// </summary>
    public bool WholeFile { get; set; }

    /// <summary>
    /// Gets the number of lines of context to ask git for.
    /// </summary>
    public int ContextLines => WholeFile ? GitChangesReader.WholeFileContext : Context;

    /// <summary>
    /// Check whether the file was opened or marked as viewed and has not changed since.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <returns>True when the file is viewed.</returns>
    public bool IsViewed(GitChangedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return _viewed.TryGetValue(file.Path, out var stamp) && stamp == file.Stamp;
    }

    /// <summary>
    /// Mark the file as viewed or not viewed.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="viewed">Whether the file is viewed.</param>
    public void SetViewed(GitChangedFile file, bool viewed)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (viewed)
        {
            _viewed[file.Path] = file.Stamp;
        }
        else
        {
            _viewed.Remove(file.Path);
        }
    }

    /// <summary>
    /// Remember that the user opened the file's current changes, which also marks them as viewed.
    /// </summary>
    /// <param name="file">The file.</param>
    public void Look(GitChangedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        _looked[file.Path] = file.Stamp;
        _viewed[file.Path] = file.Stamp;
    }

    /// <summary>
    /// Check whether the file changed since the user last had it on screen.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <returns>True when the file has changes the user has not seen.</returns>
    public bool IsUpdated(GitChangedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return _looked.TryGetValue(file.Path, out var stamp) && stamp != file.Stamp;
    }

    /// <summary>
    /// Show the diff with its usual amount of context again.
    /// </summary>
    public void ResetContext()
    {
        Context = DefaultContext;
        WholeFile = false;
    }

    /// <summary>
    /// Check whether asking git for more context could add lines to the diff.
    /// </summary>
    /// <param name="diff">The diff, read with <paramref name="contextLines"/> lines of context.</param>
    /// <param name="contextLines">The number of context lines the diff was read with.</param>
    /// <returns>True when some block can still grow.</returns>
    public static bool CanShowMore(GitFileDiff diff, int contextLines)
    {
        ArgumentNullException.ThrowIfNull(diff);
        return !diff.IsTruncated && diff.Hunks.Any(h => CanShowMore(h, contextLines));
    }

    /// <summary>
    /// Check whether asking git for more context could add lines around a block.
    /// </summary>
    /// <remarks>
    /// Git gives each side of a block as much context as it asked for unless the file runs out, so a side with less is already complete.
    /// A side with exactly as much might end with the file too; the next read then shows that and the block stops offering more.
    /// </remarks>
    /// <param name="hunk">The block.</param>
    /// <param name="contextLines">The number of context lines the diff was read with.</param>
    /// <returns>True when the block can still grow.</returns>
    public static bool CanShowMore(GitDiffHunk hunk, int contextLines)
    {
        ArgumentNullException.ThrowIfNull(hunk);
        var lines = hunk.Lines;
        if (lines.Count == 0)
        {
            return false;
        }

        var leading = lines.TakeWhile(l => l.Kind == DiffKind.Same).Count();
        if (leading == lines.Count)
        {
            return false;
        }

        var trailing = 0;
        while (lines[lines.Count - 1 - trailing].Kind == DiffKind.Same)
        {
            trailing++;
        }

        var startsFile = lines[0].OldNumber == 1 || lines[0].NewNumber == 1;
        return (leading >= contextLines && !startsFile) || trailing >= contextLines;
    }
}
