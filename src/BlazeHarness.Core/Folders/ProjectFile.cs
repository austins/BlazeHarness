namespace BlazeHarness.Core.Folders;

/// <summary>
/// One <c>AGENTS.md</c> file that was read for a session.
/// </summary>
/// <param name="Path">The full path of the file.</param>
/// <param name="Content">The text that was read, which is empty when the size cap left no room for it.</param>
/// <param name="Truncated">Whether the file is larger than what was kept.</param>
public sealed record ProjectFile(string Path, string Content, bool Truncated);
