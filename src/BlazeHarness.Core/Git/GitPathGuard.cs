namespace BlazeHarness.Core.Git;

/// <summary>
/// Checks that a path from a repository's change list is safe to read or change.
/// </summary>
internal static class GitPathGuard
{
    /// <summary>
    /// Resolve a path relative to a repository root.
    /// </summary>
    /// <remarks>
    /// The path must be relative, have no <c>..</c> part, stay inside the root, and the file and every folder
    /// between the root and the file must not be a symbolic link, so a link can't lead outside the repository.
    /// </remarks>
    /// <param name="root">The repository root.</param>
    /// <param name="relativePath">The path relative to the root.</param>
    /// <param name="fullPath">The full path, when the path is safe.</param>
    /// <returns>Whether the path is safe.</returns>
    public static bool TryResolve(string root, string relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrEmpty(relativePath) || Path.IsPathRooted(relativePath))
        {
            return false;
        }

        var parts = relativePath.Replace(Path.DirectorySeparatorChar, '/').Split('/');
        if (parts.Any(part => part.Length == 0 || part is "." or ".."))
        {
            return false;
        }

        var rootFull = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(rootFull, relativePath));
        if (!full.StartsWith(rootFull.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var current = rootFull;
            foreach (var part in parts)
            {
                current = Path.Combine(current, part);
                if (new FileInfo(current).LinkTarget is not null)
                {
                    return false;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        fullPath = full;
        return true;
    }
}
