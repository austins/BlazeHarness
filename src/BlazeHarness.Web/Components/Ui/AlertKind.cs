namespace BlazeHarness.Web.Components.Ui;

/// <summary>
/// The kind of message an <c>Alert</c> shows.
/// </summary>
public enum AlertKind
{
    /// <summary>
    /// Something failed; screen readers announce it right away.
    /// </summary>
    Error,

    /// <summary>
    /// Something to be careful about; shown as a note.
    /// </summary>
    Warning,
}
