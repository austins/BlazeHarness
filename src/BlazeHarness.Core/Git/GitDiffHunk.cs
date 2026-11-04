namespace BlazeHarness.Core.Git;

/// <summary>
/// A run of changed lines with the unchanged lines around them.
/// </summary>
/// <param name="Header">The <c>@@ -1,3 +1,4 @@</c> line that starts the hunk.</param>
/// <param name="Lines">The lines of the hunk.</param>
public sealed record GitDiffHunk(string Header, IReadOnlyList<GitDiffLine> Lines);
