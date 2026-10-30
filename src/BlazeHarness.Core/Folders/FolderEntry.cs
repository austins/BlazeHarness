namespace BlazeHarness.Core.Folders;

/// <summary>
/// One folder in a <see cref="FolderListing"/>.
/// </summary>
/// <param name="Name">The folder's name.</param>
/// <param name="Path">The folder's full path.</param>
public sealed record FolderEntry(string Name, string Path);
