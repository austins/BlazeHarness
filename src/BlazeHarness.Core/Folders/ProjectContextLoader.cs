using BlazeHarness.Core.Tools;
using System.Text;

namespace BlazeHarness.Core.Folders;

/// <summary>
/// Finds and reads the <c>AGENTS.md</c> files that apply to a folder.
/// </summary>
/// <remarks>
/// The search starts at the folder and goes up through its parents to the nearest enclosing git root, or stops at the folder
/// when there is no git repository. A symbolic link is followed only if its target is inside that same boundary.
/// </remarks>
internal static class ProjectContextLoader
{
    /// <summary>
    /// The most bytes of <c>AGENTS.md</c> text kept for one session, shared by all of its files.
    /// </summary>
    public const int MaxBytes = 32 * 1024;

    private const string FileName = "AGENTS.md";

    /// <summary>
    /// Read the <c>AGENTS.md</c> files that apply to a folder.
    /// </summary>
    /// <param name="folderPath">The full path of the folder, in the form the registry stores it.</param>
    /// <param name="cancellationToken">A token to cancel the reads.</param>
    /// <returns>The files that were found, which may be none.</returns>
    public static async Task<ProjectContext> LoadAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        var folder = Path.GetFullPath(folderPath);
        var root = FindGitRoot(folder) ?? folder;
        var boundary = WorkspacePath.CanonicalRoot(root);

        var remaining = MaxBytes;
        List<ProjectFile> nearestFirst = [];
        foreach (var directory in DirectoriesUp(folder, root))
        {
            var path = Path.Combine(directory, FileName);
            if (ResolveInside(path, boundary) is not { } real)
            {
                continue;
            }

            var (text, truncated) = await ReadAsync(real, remaining, cancellationToken);
            if (text.Length == 0 && !truncated)
            {
                continue;
            }

            remaining -= Encoding.UTF8.GetByteCount(text);
            nearestFirst.Add(new ProjectFile(path, text, truncated));
        }

        nearestFirst.Reverse();
        return new ProjectContext(folder, nearestFirst);
    }

    private static string? FindGitRoot(string folder)
    {
        for (var directory = folder; directory is not null; directory = Path.GetDirectoryName(directory))
        {
            var marker = Path.Combine(directory, ".git");
            if (Directory.Exists(marker) || File.Exists(marker))
            {
                return directory;
            }
        }

        return null;
    }

    private static IEnumerable<string> DirectoriesUp(string folder, string root)
    {
        for (var directory = folder; directory is not null; directory = Path.GetDirectoryName(directory))
        {
            yield return directory;
            if (FolderPaths.Comparer.Equals(directory, root))
            {
                yield break;
            }
        }
    }

    private static string? ResolveInside(string path, string boundary)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is not null)
            {
                if (info.ResolveLinkTarget(returnFinalTarget: true) is not FileInfo { Exists: true } target)
                {
                    return null;
                }

                var real = WorkspacePath.CanonicalRoot(target.FullName);
                return WorkspacePath.IsInside(boundary, real) && HasContent(target) ? real : null;
            }

            return info.Exists && HasContent(info) ? path : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // A named pipe or device reports a length of zero, and opening a pipe would block forever, so only files with a size are read.
    private static bool HasContent(FileInfo file) => file.Length > 0;

    private static async Task<(string Text, bool Truncated)> ReadAsync(string path, int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return (string.Empty, true);
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.Asynchronous);
            var buffer = new byte[limit + 1];
            var read = 0;
            while (read < buffer.Length)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken);
                if (count == 0)
                {
                    break;
                }

                read += count;
            }

            var truncated = read > limit;
            var text = Encoding.UTF8.GetString(buffer, 0, Math.Min(read, limit)).TrimStart('\uFEFF');
            if (truncated)
            {
                text = text.TrimEnd('\uFFFD');
            }

            return (string.IsNullOrWhiteSpace(text) ? string.Empty : text, truncated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (string.Empty, false);
        }
    }
}
