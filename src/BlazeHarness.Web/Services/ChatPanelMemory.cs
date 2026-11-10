using BlazeHarness.Core.Git;

namespace BlazeHarness.Web.Services;

/// <summary>
/// The side panel of one chat as the user left it, so switching to another chat and back finds it the same.
/// </summary>
internal sealed class ChatPanelMemory
{
    /// <summary>
    /// Gets what the user did while reading the chat's changes: viewed files, remarks, the selected file and the filter.
    /// </summary>
    public ChangesReview Review { get; } = new();

    /// <summary>
    /// Gets or sets which changes the panel lists.
    /// </summary>
    public GitChangeScope Scope { get; set; } = GitChangeScope.All;

    /// <summary>
    /// Gets or sets a value indicating whether the panel has been open or closed in this chat, so the remembered state applies.
    /// </summary>
    public bool IsKnown { get; set; }

    /// <summary>
    /// Gets or sets the tab the panel showed, or null when it was closed.
    /// </summary>
    public SidePanelTab? OpenTab { get; set; }
}
