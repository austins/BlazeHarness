using BlazeHarness.Core.Tools;

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
        return full is null ? null : WorkspacePath.CanonicalRoot(full);
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
