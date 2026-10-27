namespace BlazeHarness.Core.Diff;

/// <summary>
/// How a line differs between the old and new text.
/// </summary>
public enum DiffKind
{
    /// <summary>
    /// The line is in both texts.
    /// </summary>
    Same,

    /// <summary>
    /// The line is only in the old text.
    /// </summary>
    Removed,

    /// <summary>
    /// The line is only in the new text.
    /// </summary>
    Added,
}
