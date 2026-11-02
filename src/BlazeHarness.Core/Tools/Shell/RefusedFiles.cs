using BlazeHarness.Core.Tools.Files;

namespace BlazeHarness.Core.Tools.Shell;

/// <summary>
/// Finds the files and folders the user's rules refuse, so a sandboxed command can be kept away from them.
/// </summary>
internal static class RefusedFiles
{
    /// <summary>The most entries a walk looks at before it gives up.</summary>
    public const int MaxEntries = 200_000;

    /// <summary>The most refused paths a command is started with.</summary>
    public const int MaxPaths = 1_000;

    /// <summary>
    /// Look through the working folder and the folders a command may also read for refused paths.
    /// </summary>
    /// <param name="context">The call's context, which holds the refusing rules and the folders to look in.</param>
    /// <param name="maxEntries">The most entries to look at.</param>
    /// <param name="maxPaths">The most refused paths to collect.</param>
    /// <param name="paths">The refused files and folders that exist now. A refused folder is listed without what is inside it.</param>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>False when a limit was reached, so the list would be incomplete and the caller must not run the command.</returns>
    public static bool TryFind(ToolContext context, int maxEntries, int maxPaths, out List<string> paths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        paths = [];
        var seen = 0;
        var found = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        // A folder inside another one to walk is reached by that walk, so walking it again would only count its entries twice.
        List<string> roots = [context.Root, .. context.ShellReadFolders];
        foreach (var root in roots.Where(root => !roots.Exists(other => other != root && RootedPath.IsInside(other, root))).Distinct(StringComparer.Ordinal).Reverse())
        {
            pending.Push(root);
        }

        while (pending.TryPop(out var folder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<FileSystemInfo> children;
            try
            {
                children = [.. new DirectoryInfo(folder).EnumerateFileSystemInfos()];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                if (++seen > maxEntries)
                {
                    return false;
                }

                if (context.Refused.IsRefused(child.FullName))
                {
                    if (found.Add(child.FullName) && found.Count > maxPaths)
                    {
                        return false;
                    }

                    continue;
                }

                // A link is not followed: what it points at is reached, and checked, by its own path.
                if (child.Attributes.HasFlag(FileAttributes.Directory) && child.LinkTarget is null && !context.Sensitive.IsProtected(child.FullName))
                {
                    pending.Push(child.FullName);
                }
            }
        }

        paths = [.. found];
        return true;
    }
}
