namespace BlazeHarness.Core.Tools;

/// <summary>
/// Walks the working folder for the listing and search tools.
/// </summary>
/// <remarks>
/// Entries come in a stable order: each folder's files and folders sorted by name. <c>.git</c> and the protected folders of the context
/// are always skipped, and so is anything a <c>.gitignore</c> file between the working folder and the entry excludes. Links are listed but never followed.
/// </remarks>
internal static class WorkspaceWalker
{
    /// <summary>
    /// Walk a folder.
    /// </summary>
    /// <param name="context">The working folder.</param>
    /// <param name="start">The real path of the folder to start in, inside the working folder.</param>
    /// <param name="recursive">Whether to go into folders.</param>
    /// <param name="respectIgnore">Whether to skip what <c>.gitignore</c> files exclude.</param>
    /// <param name="cancellationToken">A token that stops the walk.</param>
    /// <returns>The entries, folders before their contents.</returns>
    public static IEnumerable<WalkEntry> Walk(ToolContext context, string start, bool recursive, bool respectIgnore, CancellationToken cancellationToken)
    {
        var rules = new List<(string Folder, GitIgnore Rules)>();
        if (respectIgnore)
        {
            foreach (var folder in FoldersFromRootTo(context.Root, start))
            {
                if (GitIgnore.Load(folder) is { } loaded)
                {
                    rules.Add((folder, loaded));
                }
            }
        }

        return WalkFolder(context, start, recursive, rules, cancellationToken);
    }

    private static IEnumerable<string> FoldersFromRootTo(string root, string start)
    {
        var chain = new List<string>();
        for (var current = start; current is not null && WorkspacePath.IsInside(root, current); current = Path.GetDirectoryName(current))
        {
            chain.Add(current);
            if (Folders.FolderPaths.Comparer.Equals(Path.TrimEndingDirectorySeparator(current), Path.TrimEndingDirectorySeparator(root)))
            {
                break;
            }
        }

        chain.Reverse();
        return chain;
    }

    private static IEnumerable<WalkEntry> WalkFolder(
        ToolContext context,
        string folder,
        bool recursive,
        List<(string Folder, GitIgnore Rules)> rules,
        CancellationToken cancellationToken)
    {
        List<FileSystemInfo> children;
        try
        {
            children = [.. new DirectoryInfo(folder).EnumerateFileSystemInfos().OrderBy(c => c.Name, StringComparer.Ordinal)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var child in children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isDirectory = child.Attributes.HasFlag(FileAttributes.Directory);
            if (isDirectory && child.Name == ".git")
            {
                continue;
            }

            if (IsIgnored(rules, child.FullName, isDirectory) || (isDirectory && context.Sensitive.IsProtected(child.FullName)))
            {
                continue;
            }

            var isLink = child.LinkTarget is not null;
            yield return new WalkEntry(child.FullName, context.Relative(child.FullName), isDirectory, isLink);
            if (!recursive || !isDirectory || isLink)
            {
                continue;
            }

            var nested = GitIgnore.Load(child.FullName);
            if (nested is not null)
            {
                rules.Add((child.FullName, nested));
            }

            foreach (var entry in WalkFolder(context, child.FullName, recursive, rules, cancellationToken))
            {
                yield return entry;
            }

            if (nested is not null)
            {
                rules.RemoveAt(rules.Count - 1);
            }
        }
    }

    private static bool IsIgnored(List<(string Folder, GitIgnore Rules)> rules, string fullPath, bool isDirectory)
    {
        var ignored = false;
        foreach (var (folder, set) in rules)
        {
            var relative = Path.GetRelativePath(folder, fullPath).Replace('\\', '/');
            if (set.Check(relative, isDirectory) is { } verdict)
            {
                ignored = verdict;
            }
        }

        return ignored;
    }
}
