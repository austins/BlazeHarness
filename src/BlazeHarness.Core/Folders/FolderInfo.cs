namespace BlazeHarness.Core.Folders;

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
