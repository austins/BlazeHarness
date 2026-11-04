using BlazeHarness.Core.Diff;

namespace BlazeHarness.Core.Git;

/// <summary>
/// One line of a file's diff.
/// </summary>
/// <param name="Kind">Whether the line is unchanged context, was added or was removed.</param>
/// <param name="OldNumber">The line's number in the old file, or null for an added line.</param>
/// <param name="NewNumber">The line's number in the new file, or null for a removed line.</param>
/// <param name="Text">The line without its line ending.</param>
public sealed record GitDiffLine(DiffKind Kind, int? OldNumber, int? NewNumber, string Text);
