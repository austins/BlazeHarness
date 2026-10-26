namespace BlazeHarness.Core.Folders;

/// <summary>
/// Why a folder path could not be used.
/// </summary>
public enum FolderError
{
    /// <summary>There was no problem.</summary>
    None,

    /// <summary>The path was empty.</summary>
    Empty,

    /// <summary>The path was not absolute, so it does not name one place.</summary>
    NotAbsolute,

    /// <summary>Nothing exists at the path.</summary>
    NotFound,

    /// <summary>The path names a file, not a folder.</summary>
    NotAFolder,

    /// <summary>The folder exists but the app is not allowed to read it.</summary>
    AccessDenied,
}

/// <summary>
/// A folder the user opened.
/// </summary>
public sealed record FolderInfo
{
    /// <summary>
    /// Gets the normalized full path, which sessions refer to.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Gets the name shown in the sidebar, which starts as the last part of the path.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets when the folder was last opened or had a session started in it, which orders the recent list.
    /// </summary>
    public DateTimeOffset LastOpenedAt { get; init; }
}

/// <summary>
/// The contents of <c>folders.json</c>.
/// </summary>
public sealed record FoldersDocument
{
    /// <summary>
    /// Gets the folders the user opened.
    /// </summary>
    public IReadOnlyList<FolderInfo> Folders { get; init; } = [];
}
