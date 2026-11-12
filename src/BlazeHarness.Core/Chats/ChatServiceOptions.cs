using BlazeHarness.Core.Chats.Titles;
using BlazeHarness.Core.Compaction;
using BlazeHarness.Core.Folders;
using BlazeHarness.Core.Mcp;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Providers;
using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Skills;
using BlazeHarness.Core.Web;

namespace BlazeHarness.Core.Chats;

/// <summary>
/// Optional services and settings wired into <see cref="ChatService"/>.
/// </summary>
public sealed record ChatServiceOptions
{
    /// <summary>
    /// Gets the default maximum number of idle, unattached chats kept open in memory before the oldest is evicted.
    /// </summary>
    public const int DefaultMaxIdleChats = 12;

    /// <summary>
    /// Gets the title generator run after a chat's first exchange, or null to keep placeholder titles.
    /// </summary>
    public ITitleGenerator? Titles { get; init; }

    /// <summary>
    /// Gets the shell sandbox; when null, commands only run in chats with <see cref="AccessLevel.Unrestricted"/>.
    /// </summary>
    public IShellSandbox? Sandbox { get; init; }

    /// <summary>
    /// Gets the global permission settings, or null to use built-in defaults.
    /// </summary>
    public PermissionDefaults? Defaults { get; init; }

    /// <summary>
    /// Gets the MCP server manager, or null when chats have no MCP tools.
    /// </summary>
    public McpManager? Mcp { get; init; }

    /// <summary>
    /// Gets the skill catalog, or null when chats have no skills.
    /// </summary>
    public SkillCatalog? Skills { get; init; }

    /// <summary>
    /// Gets a callback returning the active compaction mode, or null to summarize with the model.
    /// </summary>
    public Func<CompactionMode>? CompactionMode { get; init; }

    /// <summary>
    /// Gets the web search, fetch and browser services, or null when chats have no web tools.
    /// </summary>
    public WebServices? Web { get; init; }

    /// <summary>
    /// Gets a callback telling whether chats receive the <c>update_todos</c> tool, or null to always enable it.
    /// </summary>
    public Func<bool>? TodosEnabled { get; init; }

    /// <summary>
    /// Gets a callback returning the user's custom instructions, or null for none.
    /// </summary>
    public Func<string?>? CustomInstructions { get; init; }

    /// <summary>
    /// Gets the provider service used when another chat sends a message, or null to omit <c>send_to_chat</c>.
    /// </summary>
    public ProviderService? Providers { get; init; }

    /// <summary>
    /// Gets the open folder registry used by <c>send_to_chat</c>, or null.
    /// </summary>
    public FolderRegistry? Folders { get; init; }

    /// <summary>
    /// Gets the maximum number of idle chats without active UI listeners or background shells kept in memory.
    /// </summary>
    public int MaxIdleChats { get; init; } = DefaultMaxIdleChats;
}
