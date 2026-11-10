using BlazeHarness.Core.Diff;
using System.Text;

namespace BlazeHarness.Core.Git;

/// <summary>
/// Undoes changes to files of a git repository.
/// </summary>
/// <remarks>
/// This is the only place that changes the user's files, and it must only be called when the user explicitly asked for it,
/// after a confirmation. Every method checks that the path stays inside the repository and passes no symbolic link.
/// </remarks>
public sealed class GitChangeReverter
{
    private const int MaxHunkFileBytes = 8 * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly IGitRunner _git;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitChangeReverter"/> class.
    /// </summary>
    /// <param name="time">The clock that times out a slow git program.</param>
    public GitChangeReverter(TimeProvider time)
        : this(new GitProcessRunner(time))
    {
    }

    internal GitChangeReverter(IGitRunner git)
    {
        _git = git;
    }

    /// <summary>
    /// Undo all the changes to one file, so it is as it was in the last commit, or delete it when the commit has no such file.
    /// </summary>
    /// <remarks>
    /// A file that git does not track is deleted. In the <see cref="GitChangeScope.Unstaged"/> scope a tracked file is restored from the
    /// index, so staged changes stay. Staged changes alone and files in conflict are not touched.
    /// </remarks>
    /// <param name="root">The repository root, from <see cref="GitStatus.Root"/>.</param>
    /// <param name="scope">The scope the file was listed in.</param>
    /// <param name="file">A file from <see cref="GitStatus.Files"/>.</param>
    /// <param name="cancellationToken">A token that stops the work.</param>
    /// <returns>What happened.</returns>
    public async Task<GitRevertResult> RevertFileAsync(string root, GitChangeScope scope, GitChangedFile file, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentNullException.ThrowIfNull(file);

        if (scope == GitChangeScope.Staged || file.Kind == GitChangeKind.Conflicted || !GitPathGuard.TryResolve(root, file.Path, out var full))
        {
            return GitRevertResult.NotAllowed;
        }

        if (file.Kind == GitChangeKind.Untracked)
        {
            return await Task.Run(() => DeleteFile(full), cancellationToken);
        }

        if (scope == GitChangeScope.Unstaged)
        {
            return await RunAsync(root, ["restore", "--worktree", "--", file.Path], cancellationToken);
        }

        var head = await _git.RunAsync(root, ["rev-parse", "--verify", "--quiet", "HEAD"], cancellationToken);
        if (head.ExitCode == GitResult.Unavailable.ExitCode)
        {
            return GitRevertResult.Failed;
        }

        var inHead = false;
        if (head.Succeeded)
        {
            var exists = await _git.RunAsync(root, ["cat-file", "-e", $"HEAD:{file.Path}"], cancellationToken);
            if (exists.ExitCode == GitResult.Unavailable.ExitCode)
            {
                return GitRevertResult.Failed;
            }

            inHead = exists.Succeeded;
        }

        return await RunAsync(
            root,
            inHead ? ["restore", "--source=HEAD", "--staged", "--worktree", "--", file.Path] : ["rm", "-f", "--", file.Path],
            cancellationToken);
    }

#pragma warning disable CA1822, S2325 // An instance method like its siblings, so callers use the one object (and can substitute it) for all of its operations.
    /// <summary>
    /// Undo one hunk of a file's changes in the working tree, without git.
    /// </summary>
    /// <remarks>
    /// Only the working-tree file changes: the hunk's lines are put back as they were on its old side. The file's line ending
    /// style and whether it ends with a line break are kept. The hunk must come from a diff of the file's current content;
    /// when the lines it covers differ from that, nothing is done.
    /// </remarks>
    /// <param name="root">The repository root, from <see cref="GitStatus.Root"/>.</param>
    /// <param name="scope">The scope the hunk was read in; <see cref="GitChangeScope.Staged"/> is not allowed.</param>
    /// <param name="file">The modified or added file, from <see cref="GitStatus.Files"/>.</param>
    /// <param name="hunk">A hunk from <see cref="GitChangesReader.GetDiffAsync"/>, whole and not cut off.</param>
    /// <param name="cancellationToken">A token that stops the work.</param>
    /// <returns>What happened.</returns>
    public async Task<GitRevertResult> RevertHunkAsync(string root, GitChangeScope scope, GitChangedFile file, GitDiffHunk hunk, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(hunk);

        if (scope == GitChangeScope.Staged
            || file.Kind is not (GitChangeKind.Modified or GitChangeKind.Added)
            || !GitPathGuard.TryResolve(root, file.Path, out var full)
            || !GitHunkHeader.TryParse(hunk.Header, out var header))
        {
            return GitRevertResult.NotAllowed;
        }

        var newSide = hunk.Lines.Where(line => line.Kind != DiffKind.Removed).Select(line => line.Text).ToList();
        var oldSide = hunk.Lines.Where(line => line.Kind != DiffKind.Added).Select(line => line.Text).ToList();
        if (newSide.Count != header.NewCount || oldSide.Count != header.OldCount)
        {
            return GitRevertResult.NotAllowed;
        }

        return await Task.Run(() => RewriteFile(full, header, newSide, oldSide), cancellationToken);
    }
#pragma warning restore CA1822, S2325

    private static GitRevertResult DeleteFile(string full)
    {
        if (!File.Exists(full))
        {
            return GitRevertResult.Changed;
        }

        try
        {
            File.Delete(full);
            return GitRevertResult.Reverted;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return GitRevertResult.Failed;
        }
    }

    private static GitRevertResult RewriteFile(string full, GitHunkHeader header, List<string> newSide, List<string> oldSide)
    {
        string? temp = null;
        try
        {
            if (!File.Exists(full))
            {
                return GitRevertResult.Changed;
            }

            byte[] bytes;
            using (var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length > MaxHunkFileBytes)
                {
                    return GitRevertResult.NotAllowed;
                }

                bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);
            }

            string text;
            try
            {
                text = StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return GitRevertResult.NotAllowed;
            }

            var lines = SplitLines(text);
            var start = header.NewCount == 0 ? header.NewStart : header.NewStart - 1;
            if (start < 0 || start + header.NewCount > lines.Count)
            {
                return GitRevertResult.Changed;
            }

            for (var i = 0; i < newSide.Count; i++)
            {
                if (lines[start + i].Text != newSide[i])
                {
                    return GitRevertResult.Changed;
                }
            }

            var endedWithLineBreak = lines.Count > 0 && lines[^1].Ending.Length > 0;
            var ending = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            lines.RemoveRange(start, header.NewCount);
            lines.InsertRange(start, oldSide.Select(line => new Line(line, ending)));
            if (lines.Count > 0)
            {
                var last = lines[^1];
                lines[^1] = last with { Ending = endedWithLineBreak ? (last.Ending.Length > 0 ? last.Ending : ending) : string.Empty };
            }

            var result = new StringBuilder(text.Length);
            foreach (var line in lines)
            {
                result.Append(line.Text).Append(line.Ending);
            }

            temp = Path.Combine(Path.GetDirectoryName(full)!, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(temp, StrictUtf8.GetBytes(result.ToString()));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temp, File.GetUnixFileMode(full));
            }

            File.Move(temp, full, overwrite: true);
            temp = null;
            return GitRevertResult.Reverted;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return GitRevertResult.Failed;
        }
        finally
        {
            if (temp is not null)
            {
                try
                {
                    File.Delete(temp);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The leftover temporary file is harmless; the revert has already failed.
                }
            }
        }
    }

    private static List<Line> SplitLines(string text)
    {
        List<Line> lines = [];
        var position = 0;
        while (position < text.Length)
        {
            var newline = text.IndexOf('\n', position);
            if (newline < 0)
            {
                lines.Add(new Line(text[position..], string.Empty));
                break;
            }

            var crlf = newline > position && text[newline - 1] == '\r';
            lines.Add(new Line(text[position..(crlf ? newline - 1 : newline)], crlf ? "\r\n" : "\n"));
            position = newline + 1;
        }

        return lines;
    }

    private async Task<GitRevertResult> RunAsync(string root, string[] arguments, CancellationToken cancellationToken)
        => (await _git.RunAsync(root, arguments, cancellationToken)).Succeeded ? GitRevertResult.Reverted : GitRevertResult.Failed;

    /// <summary>
    /// One line of a file with the line break that ended it.
    /// </summary>
    /// <param name="Text">The line without its line break.</param>
    /// <param name="Ending">The line break, or empty for a last line that has none.</param>
    private sealed record Line(string Text, string Ending);
}
