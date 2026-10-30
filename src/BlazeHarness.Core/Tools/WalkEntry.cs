namespace BlazeHarness.Core.Tools;

/// <summary>
/// One file or folder found while walking the working folder.
/// </summary>
/// <param name="FullPath">The full path.</param>
/// <param name="RelativePath">The path relative to the working folder, with forward slashes.</param>
/// <param name="IsDirectory">Whether the entry is a folder.</param>
/// <param name="IsLink">Whether the entry is a symbolic link or junction, which is never followed.</param>
internal sealed record WalkEntry(string FullPath, string RelativePath, bool IsDirectory, bool IsLink);
