namespace BlazeHarness.Core.Folders;

/// <summary>
/// Lists folders on the machine the app runs on, because a web page cannot hand the server a real path.
/// </summary>
/// <remarks>
/// Only folder names are returned, never file names or contents. The app only accepts local connections.
/// </remarks>
public static class FolderBrowser
{
    /// <summary>
    /// List the folders inside a folder.
    /// </summary>
    /// <param name="path">The folder to list, or null for the user's home folder. A leading <c>~</c> means the home folder.</param>
    /// <param name="userHome">The user's home folder.</param>
    /// <param name="includeHidden">Whether to include folders that start with a dot or are marked hidden.</param>
    /// <returns>The listing, with an error and no folders when the path cannot be listed.</returns>
    public static FolderListing List(string? path, string userHome, bool includeHidden = false)
    {
        ArgumentNullException.ThrowIfNull(userHome);
        var full = string.IsNullOrWhiteSpace(path) ? userHome : FolderPaths.ToFullPath(path, userHome);
        if (full is null)
        {
            return new FolderListing(null, null, [], string.IsNullOrWhiteSpace(path) ? FolderError.Empty : FolderError.NotAbsolute);
        }

        if (!Directory.Exists(full))
        {
            return new FolderListing(null, null, [], File.Exists(full) ? FolderError.NotAFolder : FolderError.NotFound);
        }

        var parent = Path.GetDirectoryName(full);
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = includeHidden ? FileAttributes.System : FileAttributes.Hidden | FileAttributes.System,
        };

        try
        {
            var folders = new DirectoryInfo(full)
                .EnumerateDirectories("*", options)
                .Where(d => includeHidden || !d.Name.StartsWith('.'))
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(d => new FolderEntry(d.Name, d.FullName))
                .ToList();
            return new FolderListing(full, parent, folders, FolderError.None);
        }
        catch (UnauthorizedAccessException)
        {
            return new FolderListing(full, parent, [], FolderError.AccessDenied);
        }
        catch (IOException)
        {
            return new FolderListing(null, null, [], FolderError.NotFound);
        }
    }

    /// <summary>
    /// Get the starting points for the picker: the home folder, and the top of each drive on Windows or the root elsewhere.
    /// </summary>
    /// <param name="userHome">The user's home folder.</param>
    /// <returns>The places, with the home folder first.</returns>
    public static IReadOnlyList<FolderEntry> Places(string userHome)
    {
        ArgumentNullException.ThrowIfNull(userHome);
        List<FolderEntry> places = [new FolderEntry("Home", userHome)];
        if (!OperatingSystem.IsWindows())
        {
            places.Add(new FolderEntry("/", Path.GetPathRoot(userHome) ?? "/"));
            return places;
        }

        try
        {
            places.AddRange(DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => new FolderEntry(d.Name, d.RootDirectory.FullName)));
        }
        catch (IOException)
        {
            // Without a drive list the home folder is still a usable start.
        }

        return places;
    }
}
