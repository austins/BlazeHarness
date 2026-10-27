using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Permissions;

/// <summary>
/// Folders whose content a tool must never read or write, such as keys and the app's own settings.
/// </summary>
public sealed class SensitivePaths
{
    private readonly List<string> _folders;

    /// <summary>
    /// Initializes a new instance of the <see cref="SensitivePaths"/> class.
    /// </summary>
    /// <param name="folders">The full paths of the protected folders.</param>
    /// <param name="exempt">A folder inside a protected one that may still be used, such as the current session's own directory, or null.</param>
    public SensitivePaths(IEnumerable<string> folders, string? exempt = null)
    {
        ArgumentNullException.ThrowIfNull(folders);
        _folders = [.. folders.Select(Normalize)];
        Exempt = exempt is null ? null : Normalize(exempt);
    }

    /// <summary>
    /// Gets a list that protects nothing.
    /// </summary>
    public static SensitivePaths None { get; } = new([]);

    /// <summary>
    /// Gets the protected folders.
    /// </summary>
    public IReadOnlyList<string> Folders => _folders;

    /// <summary>
    /// Gets the folder that is allowed even though it is inside a protected one, or null.
    /// </summary>
    public string? Exempt { get; }

    /// <summary>
    /// Get the folders that are protected unless the user changes the list: credential stores and browser profiles.
    /// </summary>
    /// <param name="paths">The app's folders.</param>
    /// <returns>The full paths of the folders. The app's own folder is not in it, because it is always protected.</returns>
    public static IReadOnlyList<string> DefaultFolders(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var home = paths.UserHome;
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        List<string> folders =
        [
            Path.Combine(home, ".ssh"),
            Path.Combine(home, ".aws"),
            Path.Combine(home, ".azure"),
            Path.Combine(home, ".gnupg"),
            Path.Combine(home, ".kube"),
            Path.Combine(home, ".docker"),
            Path.Combine(home, ".microsoft", "usersecrets"),
            Path.Combine(home, ".mozilla"),
            Path.Combine(home, ".config", "google-chrome"),
            Path.Combine(home, ".config", "chromium"),
            Path.Combine(home, ".config", "BraveSoftware"),
            Path.Combine(home, ".config", "microsoft-edge"),
            Path.Combine(home, "Library", "Application Support", "Google", "Chrome"),
            Path.Combine(home, "Library", "Application Support", "Firefox"),
            Path.Combine(home, "Library", "Keychains"),
        ];

        if (!string.IsNullOrEmpty(roaming))
        {
            folders.Add(Path.Combine(roaming, "Microsoft", "UserSecrets"));
            folders.Add(Path.Combine(roaming, "Mozilla"));
        }

        if (!string.IsNullOrEmpty(local))
        {
            folders.Add(Path.Combine(local, "Google", "Chrome", "User Data"));
            folders.Add(Path.Combine(local, "Microsoft", "Edge", "User Data"));
        }

        return folders;
    }

    /// <summary>
    /// Build the list: the folders the user chose or the defaults, and the app's own folder.
    /// </summary>
    /// <param name="paths">The app's folders.</param>
    /// <param name="currentSession">The id of the session that is running, whose own directory stays usable, or null.</param>
    /// <param name="chosen">The folders the user chose to protect, or null for <see cref="DefaultFolders"/>.</param>
    /// <returns>The protected folders.</returns>
    public static SensitivePaths Default(AppPaths paths, string? currentSession = null, IEnumerable<string>? chosen = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        List<string> folders = [.. chosen ?? DefaultFolders(paths), paths.Home];
        return new SensitivePaths(folders, currentSession is null ? null : paths.SessionDirectory(currentSession));
    }

    /// <summary>
    /// Get whether a path may not be used.
    /// </summary>
    /// <param name="fullPath">The real full path a call would touch.</param>
    /// <returns>True when the path is inside a protected folder and not inside the exempt one.</returns>
    public bool IsProtected(string fullPath)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        var path = Normalize(fullPath);
        if (Exempt is not null && WorkspacePath.IsInside(Exempt, path))
        {
            return false;
        }

        return _folders.Exists(folder => WorkspacePath.IsInside(folder, path));
    }

    private static string Normalize(string path) => WorkspacePath.CanonicalRoot(path);
}
