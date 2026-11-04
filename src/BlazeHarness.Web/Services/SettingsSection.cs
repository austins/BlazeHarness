namespace BlazeHarness.Web.Services;

/// <summary>
/// A section of the settings dialog.
/// </summary>
public enum SettingsSection
{
    /// <summary>
    /// The providers and their models.
    /// </summary>
    Providers,

    /// <summary>
    /// The permission defaults for new chats.
    /// </summary>
    Permissions,

    /// <summary>
    /// The MCP servers.
    /// </summary>
    Mcp,

    /// <summary>
    /// The skills.
    /// </summary>
    Skills,

    /// <summary>
    /// The web search and fetch tools.
    /// </summary>
    Web,

    /// <summary>
    /// Compaction, the to-do list, chat appearance and keyboard shortcuts.
    /// </summary>
    General,

    /// <summary>
    /// Paths, versions and status of the app.
    /// </summary>
    Diagnostics,
}
