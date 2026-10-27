namespace BlazeHarness.Core.Tools;

/// <summary>
/// Resolves paths a tool is given against a root folder and refuses anything that leads outside it.
/// </summary>
/// <remarks>
/// A path is checked twice: as written, after removing <c>..</c> parts, and again after following every symbolic link
/// and junction on the way, so a link inside the folder can't lead out of it.
/// </remarks>
public static class WorkspacePath
{
    private const int MaxLinkDepth = 32;

    /// <summary>
    /// Resolve a path from a tool call.
    /// </summary>
    /// <param name="root">The root folder, as returned by <see cref="CanonicalRoot"/>.</param>
    /// <param name="input">The path as the model wrote it: relative to the root, or absolute but inside it. Blank means the root.</param>
    /// <param name="fullPath">The real path to use, when the result is true.</param>
    /// <param name="error">A message for the model, when the result is false.</param>
    /// <param name="allowOutside">True when the call was approved to use a path outside the root.</param>
    /// <returns>True when the path is inside the root, or <paramref name="allowOutside"/> is set.</returns>
    public static bool TryResolve(string root, string? input, out string fullPath, out string error, bool allowOutside = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        if (!TryLocate(root, input, out fullPath))
        {
            error = $"The path '{input}' is not valid.";
            return false;
        }

        error = string.Empty;
        if (!allowOutside && !IsInside(root, fullPath))
        {
            error = $"The path '{input}' is outside the working folder '{root}'. Only paths inside it can be used.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Find the real path a tool call names, wherever it is.
    /// </summary>
    /// <remarks>
    /// Links are followed, so the result is what the call would really touch. Use it to decide whether a call may run, not to open the file.
    /// </remarks>
    /// <param name="root">The root folder that relative paths resolve against.</param>
    /// <param name="input">The path as the model wrote it. Blank means the root.</param>
    /// <param name="fullPath">The real path, when the result is true.</param>
    /// <returns>False when the path is not valid or its links loop.</returns>
    public static bool TryLocate(string root, string? input, out string fullPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        fullPath = root;

        var text = string.IsNullOrWhiteSpace(input) ? "." : input.Trim();
        if (text.Contains('\0', StringComparison.Ordinal) || (OperatingSystem.IsWindows() && HasStreamOrDevicePrefix(text)))
        {
            return false;
        }

        string combined;
        try
        {
            combined = Path.GetFullPath(Path.IsPathRooted(text) ? text : Path.Combine(root, text));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (RealPath(combined, 0) is not { } real)
        {
            return false;
        }

        fullPath = real;
        return true;
    }

    /// <summary>
    /// Get the real path of a folder, with every link followed, for use as a root.
    /// </summary>
    /// <param name="folder">The folder path.</param>
    /// <returns>The real full path, without a trailing separator.</returns>
    public static string CanonicalRoot(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        var full = Path.GetFullPath(folder);
        return Path.TrimEndingDirectorySeparator(RealPath(full, 0) ?? full);
    }

    /// <summary>
    /// Get whether a full path is the root or lies under it.
    /// </summary>
    /// <param name="root">The root folder.</param>
    /// <param name="fullPath">The path to test, already resolved.</param>
    /// <returns>True when the path is inside the root.</returns>
    public static bool IsInside(string root, string fullPath)
    {
        var rootTrimmed = Path.TrimEndingDirectorySeparator(root);
        var pathTrimmed = Path.TrimEndingDirectorySeparator(fullPath);
        if (Folders.FolderPaths.Comparer.Equals(rootTrimmed, pathTrimmed))
        {
            return true;
        }

        return pathTrimmed.Length > rootTrimmed.Length
            && pathTrimmed.StartsWith(rootTrimmed, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            && (pathTrimmed[rootTrimmed.Length] == Path.DirectorySeparatorChar || pathTrimmed[rootTrimmed.Length] == Path.AltDirectorySeparatorChar || rootTrimmed.EndsWith(Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// Get a path relative to the root, written with forward slashes on every platform.
    /// </summary>
    /// <param name="root">The root folder.</param>
    /// <param name="fullPath">A path inside the root.</param>
    /// <returns>The relative path, or <c>.</c> for the root itself.</returns>
    public static string Relative(string root, string fullPath)
    {
        var relative = Path.GetRelativePath(root, fullPath);
        return relative == "." || Path.DirectorySeparatorChar == '/' ? relative : relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static bool HasStreamOrDevicePrefix(string text)
    {
        if (text.StartsWith(@"\\?\", StringComparison.Ordinal) || text.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return true;
        }

        var firstColon = text.IndexOf(':', StringComparison.Ordinal);
        return firstColon >= 0 && (firstColon != 1 || text.IndexOf(':', 2) >= 0);
    }

    private static string? RealPath(string fullPath, int depth)
    {
        if (depth > MaxLinkDepth)
        {
            return null;
        }

        var current = Path.GetPathRoot(fullPath) ?? string.Empty;
        var parts = fullPath[current.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var next = Path.Combine(current, parts[i]);
            FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
            FileSystemInfo? target;
            try
            {
                if (info.LinkTarget is null)
                {
                    current = next;
                    continue;
                }

                target = info.ResolveLinkTarget(returnFinalTarget: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A loop of links, or a link or folder that can't be read, is not a path to use.
                return null;
            }

            if (target is null)
            {
                return null;
            }

            var resolved = RealPath(target.FullName, depth + 1);
            if (resolved is null)
            {
                return null;
            }

            current = resolved;
        }

        return current;
    }
}
