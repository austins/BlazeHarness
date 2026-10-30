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

    /// <summary>The folder is too broad or private to work in: the file system root, the user's home folder, or the app's own data folder.</summary>
    NotAllowed,
}
