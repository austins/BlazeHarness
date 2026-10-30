namespace BlazeHarness.Core.Folders;

/// <summary>
/// The result of opening a folder.
/// </summary>
/// <param name="Folder">The folder, or null when it could not be opened.</param>
/// <param name="Error">Why it could not be opened, or <see cref="FolderError.None"/>.</param>
public sealed record OpenFolderResult(FolderInfo? Folder, FolderError Error);
