namespace BlazeHarness.Web.Services;

/// <summary>
/// A view in the side panel next to the chat.
/// </summary>
public enum SidePanelTab
{
    /// <summary>
    /// The page the assistant has open in its browser.
    /// </summary>
    Browser,

    /// <summary>
    /// The files that changed in the git repository of the chat's folder.
    /// </summary>
    Changes,
}
