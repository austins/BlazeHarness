namespace BlazeHarness.Core.Diff;

/// <summary>
/// One line of a diff.
/// </summary>
/// <param name="Kind">How the line differs.</param>
/// <param name="Text">The line without its line ending.</param>
public sealed record DiffLine(DiffKind Kind, string Text);
