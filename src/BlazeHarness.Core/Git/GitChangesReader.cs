using BlazeHarness.Core.Diff;
using System.Globalization;
using System.Text;

namespace BlazeHarness.Core.Git;

/// <summary>
/// Looks at what changed in the git repository around a folder.
/// </summary>
/// <remarks>
/// Nothing is remembered between calls, so a repository created or emptied while a chat runs shows up on the next call.
/// It only reads: no file or repository is changed. <see cref="GitChangeReverter"/> is what changes files.
/// </remarks>
public sealed class GitChangesReader
{
    /// <summary>
    /// The number of context lines that shows a whole file in a diff.
    /// </summary>
    public const int WholeFileContext = 100000;

    private const int MaxUntrackedBytes = 512 * 1024;
    private const int BinarySniffBytes = 8000;
    private const int MaxImageBytes = 1024 * 1024;

    private readonly IGitRunner _git;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitChangesReader"/> class.
    /// </summary>
    /// <param name="time">The clock that times out a slow git program.</param>
    public GitChangesReader(TimeProvider time)
        : this(new GitProcessRunner(time))
    {
    }

    internal GitChangesReader(IGitRunner git)
    {
        _git = git;
    }

    /// <summary>
    /// Get the changed files of the repository that contains a folder.
    /// </summary>
    /// <param name="folder">The full path of a folder, at the top of the repository or anywhere below it.</param>
    /// <param name="scope">Which changes to list in <see cref="GitStatus.Files"/>.</param>
    /// <param name="cancellationToken">A token that stops the work.</param>
    /// <returns>The changes, or null when git is not installed or the folder is not inside a repository.</returns>
    public async Task<GitStatus?> GetStatusAsync(string folder, GitChangeScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        if (!Directory.Exists(folder) || await FindRootAsync(folder, cancellationToken) is not { } root)
        {
            return null;
        }

        var status = await _git.RunAsync(root, ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--no-renames"], cancellationToken);
        if (!status.Succeeded)
        {
            return null;
        }

        var entries = GitStatusParser.Parse(status.Output);
        var chosen = entries.Where(entry => scope switch
        {
            GitChangeScope.Staged => entry.IsStaged,
            GitChangeScope.Unstaged => entry.IsUnstaged,
            _ => true,
        }).ToList();

        var counts = chosen.Count == 0
            ? []
            : GitNumstatParser.Parse((await _git.RunAsync(root, [.. DiffBase(scope, await HasHeadAsync(root, cancellationToken)), "--numstat", "-z", "--no-renames", "--no-ext-diff", "--no-textconv"], cancellationToken)).Output);

        var files = chosen
            .Select(entry =>
            {
                counts.TryGetValue(entry.Path, out var count);
                var kind = KindOf(entry, scope);
                return new GitChangedFile(
                    entry.Path,
                    kind,
                    entry.IsStaged,
                    entry.IsUnstaged,
                    count.Added,
                    count.Removed,
                    IsBinary: counts.ContainsKey(entry.Path) && count.Added is null,
                    StampOf(root, entry, kind, count));
            })
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ToList();

        return new GitStatus(
            root,
            files,
            entries.Count,
            entries.Count(entry => entry.IsStaged),
            entries.Count(entry => entry.IsUnstaged));
    }

    /// <summary>
    /// Get the changes to one file.
    /// </summary>
    /// <param name="root">The repository root, from <see cref="GitStatus.Root"/>.</param>
    /// <param name="scope">Which changes to show.</param>
    /// <param name="file">A file from <see cref="GitStatus.Files"/>.</param>
    /// <param name="contextLines">How many unchanged lines to show around each change; use <see cref="WholeFileContext"/> for the whole file. Values outside 0 to <see cref="WholeFileContext"/> are brought into that range. Untracked files are always shown whole.</param>
    /// <param name="cancellationToken">A token that stops the work.</param>
    /// <returns>The diff, or null when the file has no changes to show any more.</returns>
    public async Task<GitFileDiff?> GetDiffAsync(string root, GitChangeScope scope, GitChangedFile file, int contextLines = 3, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentNullException.ThrowIfNull(file);

        if (file.Kind == GitChangeKind.Untracked)
        {
            return await Task.Run(() => ReadUntracked(root, file.Path), cancellationToken);
        }

        var hasHead = await HasHeadAsync(root, cancellationToken);
        var context = Math.Clamp(contextLines, 0, WholeFileContext);
        var result = await _git.RunAsync(
            root,
            [.. DiffBase(scope, hasHead), $"-U{context.ToString(CultureInfo.InvariantCulture)}", "--no-color", "--no-ext-diff", "--no-textconv", "--no-renames", "--", file.Path],
            cancellationToken);
        return result.Succeeded ? GitDiffParser.Parse(file.Path, result.Output, result.Truncated) : null;
    }

#pragma warning disable CA1822, S2325 // An instance method like its siblings, so callers use the one object (and can substitute it) for all of its operations.
    /// <summary>
    /// Get the picture a changed file holds in the working tree.
    /// </summary>
    /// <remarks>
    /// Only the current file is read, never an earlier version. The kind of picture is decided from the file's first bytes, not its name,
    /// and only PNG, JPEG, GIF and WebP are served: a vector format such as SVG can hold script, so it is never returned.
    /// </remarks>
    /// <param name="root">The repository root, from <see cref="GitStatus.Root"/>.</param>
    /// <param name="file">A file from <see cref="GitStatus.Files"/>.</param>
    /// <param name="cancellationToken">A token that stops the work.</param>
    /// <returns>The picture, or null when the file is deleted, in conflict, outside the root, behind a symbolic link, larger than 1 MiB, missing or not one of those picture formats.</returns>
    public async Task<GitImage?> GetImageAsync(string root, GitChangedFile file, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentNullException.ThrowIfNull(file);

        if (file.Kind is GitChangeKind.Deleted or GitChangeKind.Conflicted || !GitPathGuard.TryResolve(root, file.Path, out var full))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, useAsync: true);
            var buffer = new byte[MaxImageBytes + 1];
            var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);
            if (read > MaxImageBytes || MediaTypeOf(buffer.AsSpan(0, read)) is not { } mediaType)
            {
                return null;
            }

            return new GitImage(mediaType, buffer.AsSpan(0, read).ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
#pragma warning restore CA1822, S2325

    private static string? MediaTypeOf(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8))
        {
            return "image/gif";
        }

        return bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8) ? "image/webp" : null;
    }

    private static string StampOf(string root, GitStatusEntry entry, GitChangeKind kind, (int? Added, int? Removed) count)
    {
        string state;
        try
        {
            var info = new FileInfo(Path.Combine(root, entry.Path));
            state = info.Exists || info.LinkTarget is not null
                ? $"{info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)}:{info.Length.ToString(CultureInfo.InvariantCulture)}"
                : "deleted";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            state = "unknown";
        }

        return $"{state}:{kind}:{entry.IsStaged}:{entry.IsUnstaged}:{count.Added}:{count.Removed}";
    }

    private static string[] DiffBase(GitChangeScope scope, bool hasHead) => scope switch
    {
        GitChangeScope.Staged => ["diff", "--cached"],
        GitChangeScope.Unstaged => ["diff"],
        // Before the first commit there is nothing to compare with, so only what is staged can be shown.
        _ => hasHead ? ["diff", "HEAD"] : ["diff", "--cached"],
    };

    private static GitChangeKind KindOf(GitStatusEntry entry, GitChangeScope scope)
    {
        if (entry.IsUntracked)
        {
            return GitChangeKind.Untracked;
        }

        if (entry.IsConflicted)
        {
            return GitChangeKind.Conflicted;
        }

        return scope switch
        {
            GitChangeScope.Staged => FromLetter(entry.Index),
            GitChangeScope.Unstaged => FromLetter(entry.WorkTree),
            _ when entry.WorkTree == 'D' => GitChangeKind.Deleted,
            _ when entry.Index == 'A' => GitChangeKind.Added,
            _ => FromLetter(entry.Index != ' ' ? entry.Index : entry.WorkTree),
        };
    }

    private static GitChangeKind FromLetter(char letter) => letter switch
    {
        'A' => GitChangeKind.Added,
        'D' => GitChangeKind.Deleted,
        'T' => GitChangeKind.TypeChanged,
        _ => GitChangeKind.Modified,
    };

    /// <summary>
    /// Read an untracked file as a diff containing only added lines.
    /// </summary>
    /// <remarks>Git does not diff files it does not track.</remarks>
    /// <param name="root">The repository root.</param>
    /// <param name="relativePath">The file's path relative to the repository root.</param>
    private static GitFileDiff? ReadUntracked(string root, string relativePath)
    {
        var rootFull = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(rootFull, relativePath));
        if (!full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(full))
        {
            return null;
        }

        var info = new FileInfo(full);
        if (info.LinkTarget is not null)
        {
            return new GitFileDiff(relativePath, IsBinary: true, IsTruncated: false, []);
        }

        using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[Math.Min(info.Length, MaxUntrackedBytes)];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        if (Array.IndexOf(buffer, (byte)0, 0, Math.Min(read, BinarySniffBytes)) >= 0)
        {
            return new GitFileDiff(relativePath, IsBinary: true, IsTruncated: false, []);
        }

        var lines = new List<GitDiffLine>();
        var truncated = info.Length > MaxUntrackedBytes;
        var number = 1;
        foreach (var line in Encoding.UTF8.GetString(buffer, 0, read).Split('\n'))
        {
            if (lines.Count >= GitDiffParser.MaxLines)
            {
                truncated = true;
                break;
            }

            lines.Add(new GitDiffLine(DiffKind.Added, null, number++, line.TrimEnd('\r')));
        }

        // A file that ends with a line break has one empty piece after the last line.
        if (!truncated && lines.Count > 0 && lines[^1].Text.Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var header = $"@@ -0,0 +1,{lines.Count} @@";
        return new GitFileDiff(relativePath, IsBinary: false, truncated, lines.Count == 0 ? [] : [new GitDiffHunk(header, lines)]);
    }

    private async Task<string?> FindRootAsync(string folder, CancellationToken cancellationToken)
    {
        var result = await _git.RunAsync(folder, ["rev-parse", "--show-toplevel"], cancellationToken);
        var root = result.Output.Trim();
        return result.Succeeded && root.Length > 0 ? Path.GetFullPath(root) : null;
    }

    private async Task<bool> HasHeadAsync(string root, CancellationToken cancellationToken)
        => (await _git.RunAsync(root, ["rev-parse", "--verify", "--quiet", "HEAD"], cancellationToken)).Succeeded;
}
