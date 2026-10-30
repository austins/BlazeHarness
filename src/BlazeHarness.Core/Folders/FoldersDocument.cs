namespace BlazeHarness.Core.Folders;

/// <summary>
/// The contents of <c>folders.json</c>.
/// </summary>
internal sealed record FoldersDocument
{
    /// <summary>
    /// Gets the folders the user opened.
    /// </summary>
    public IReadOnlyList<FolderInfo> Folders { get; init; } = [];
}
