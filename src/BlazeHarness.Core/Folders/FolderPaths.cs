using BlazeHarness.Core.Tools.Files;

namespace BlazeHarness.Core.Folders;

/// <summary>
/// Turns what a user typed into one canonical path, so the same folder is never listed twice.
/// </summary>
public static class FolderPaths
{
    /// <summary>
    /// Gets the comparer for normalized paths: ignoring case where the usual file systems do (Windows and macOS).
    /// </summary>
    public static StringComparer Comparer { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Make an absolute path without trailing separators or <c>.</c> and <c>..</c> parts, without following links.
    /// </summary>
    /// <remarks>
    /// A leading <c>~</c> means the user's home folder. A relative path is rejected, because the app's own
    /// working directory is not something the user chose.
    /// </remarks>
    /// <param name="path">The path as typed.</param>
    /// <param name="userHome">The user's home folder, used for a leading <c>~</c>.</param>
    /// <returns>The full path, or null when the input is empty or not absolute.</returns>
    public static string? ToFullPath(string? path, string userHome)
    {
        ArgumentNullException.ThrowIfNull(userHome);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var text = path.Trim();
        if (text == "~")
        {
            text = userHome;
        }
        else if (text.StartsWith("~/", StringComparison.Ordinal) || text.StartsWith("~\\", StringComparison.Ordinal))
        {
            text = Path.Combine(userHome, text[2..]);
        }

        if (!Path.IsPathRooted(text) || (OperatingSystem.IsWindows() && Path.GetPathRoot(text) is "\\" or "/"))
        {
            return null;
        }

        return TrimTrailingSeparators(Path.GetFullPath(text));
    }

    /// <summary>
    /// Make the canonical form of a path: the full path with every symbolic link or junction followed.
    /// </summary>
    /// <param name="path">The path as typed.</param>
    /// <param name="userHome">The user's home folder, used for a leading <c>~</c>.</param>
    /// <returns>The canonical path, or null when the input is empty or not absolute.</returns>
    public static string? Normalize(string? path, string userHome)
    {
        var full = ToFullPath(path, userHome);
        return full is null ? null : RootedPath.CanonicalRoot(full);
    }

    /// <summary>
    /// Resolve a relative file path inside <paramref name="root"/>, following symbolic links and junctions and refusing anything that leaves the folder.
    /// </summary>
    /// <param name="root">The folder the file must stay inside.</param>
    /// <param name="relativePath">The path relative to <paramref name="root"/>.</param>
    /// <param name="realPath">The resolved real file path when the result is non-null, otherwise empty.</param>
    /// <returns>The path to open when the file exists inside <paramref name="root"/>, otherwise null.</returns>
    public static string? ResolveFileInside(string root, string relativePath, out string realPath)
    {
        realPath = string.Empty;
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(relativePath) || Path.IsPathRooted(relativePath))
        {
            return null;
        }

        var canonicalRoot = RootedPath.CanonicalRoot(root);
        if (!RootedPath.TryResolve(canonicalRoot, relativePath, out var resolved, out _) || !File.Exists(resolved))
        {
            return null;
        }

        realPath = resolved;
        var fullRoot = TrimTrailingSeparators(Path.GetFullPath(root));
        var combined = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        var sameRelativePath = RootedPath.IsInside(fullRoot, combined)
            && string.Equals(RootedPath.Relative(canonicalRoot, resolved), RootedPath.Relative(fullRoot, combined), StringComparison.Ordinal);
        return sameRelativePath ? combined : resolved;
    }

    /// <summary>
    /// Get the name to show for a folder: the last part of its path, or the root itself for a drive or <c>/</c>.
    /// </summary>
    /// <param name="fullPath">A full path.</param>
    /// <returns>The display name.</returns>
    public static string DisplayName(string fullPath)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        var name = Path.GetFileName(TrimTrailingSeparators(fullPath));
        return name.Length > 0 ? name : fullPath;
    }

    private static string TrimTrailingSeparators(string path)
    {
        var root = Path.GetPathRoot(path) ?? string.Empty;
        return path.Length > root.Length ? path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : path;
    }
}
