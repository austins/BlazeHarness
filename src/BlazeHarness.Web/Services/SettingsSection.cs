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
    /// How a chat that grows too long is compacted.
    /// </summary>
    Context,

    /// <summary>
    /// The text size.
    /// </summary>
    Appearance,

    /// <summary>
    /// Paths, versions and status of the app.
    /// </summary>
    Diagnostics,
}
