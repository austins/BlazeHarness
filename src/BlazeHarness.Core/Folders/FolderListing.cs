namespace BlazeHarness.Core.Folders;

/// <summary>
/// The folders inside one folder, for the folder picker.
/// </summary>
/// <param name="Path">The folder that was listed, or null when it could not be.</param>
/// <param name="ParentPath">The folder above it, or null at the top.</param>
/// <param name="Folders">The folders inside it, sorted by name.</param>
/// <param name="Error">Why it could not be listed, or <see cref="FolderError.None"/>.</param>
public sealed record FolderListing(string? Path, string? ParentPath, IReadOnlyList<FolderEntry> Folders, FolderError Error);
