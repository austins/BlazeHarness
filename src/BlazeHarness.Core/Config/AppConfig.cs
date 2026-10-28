using BlazeHarness.Core.Compaction;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazeHarness.Core.Config;

/// <summary>
/// The contents of <c>config.json</c>: defaults, permissions and switches. Providers live in <c>providers.json</c>.
/// </summary>
public sealed record AppConfig
{
    /// <summary>
    /// Gets the provider preselected for new chats.
    /// </summary>
    public string? DefaultProviderId { get; init; }

    /// <summary>
    /// Gets the model preselected for new chats.
    /// </summary>
    public string? DefaultModelId { get; init; }

    /// <summary>
    /// Gets an optional cheaper model for titles and compaction, as <c>providerId/modelId</c>.
    /// </summary>
    public string? SideTaskModel { get; init; }

    /// <summary>
    /// Gets the access level new chats start with.
    /// </summary>
    /// <remarks>
    /// Only <see cref="AccessLevel.ReadOnly"/> and <see cref="AccessLevel.WriteInFolder"/> count; anything else is treated as the built-in default.
    /// </remarks>
    public AccessLevel DefaultAccess { get; init; } = AccessLevel.WriteInFolder;

    /// <summary>
    /// Gets the approval mode new chats start with.
    /// </summary>
    /// <remarks>
    /// Only <see cref="ApprovalMode.AskFirst"/> and <see cref="ApprovalMode.AutoApprove"/> count; anything else is treated as the built-in default.
    /// </remarks>
    public ApprovalMode DefaultApprovals { get; init; } = ApprovalMode.AskFirst;

    /// <summary>
    /// Gets the rules the user wrote to allow, ask about or refuse tool calls.
    /// </summary>
    public IReadOnlyList<PermissionRule> PermissionRules { get; init; } = [];

    /// <summary>
    /// Gets the folders no tool may touch, or null to use the built-in list.
    /// </summary>
    public IReadOnlyList<string>? ProtectedFolders { get; init; }

    /// <summary>
    /// Gets the names of the MCP servers the user trusts, whose read-only tools may run without a question.
    /// </summary>
    /// <remarks>
    /// It lives here and not in <c>mcp.json</c>, so a pasted server snippet can't grant itself trust.
    /// </remarks>
    public IReadOnlyList<string> TrustedMcpServers { get; init; } = [];

    /// <summary>
    /// Gets the names of the skills that are turned off for every chat, unless a chat turns one back on.
    /// </summary>
    public IReadOnlyList<string> DisabledSkills { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the Unrestricted and Never-ask options are hidden.
    /// </summary>
    public bool DisableBypassModes { get; init; }

    /// <summary>
    /// Gets how the summary that replaces older messages is written; the model writes it by default.
    /// </summary>
    public CompactionMode Compaction { get; init; } = CompactionMode.Model;

    /// <summary>
    /// Gets the switches and search providers of the web search and web fetch tools.
    /// </summary>
    public WebSettings Web { get; init; } = new();

    /// <summary>
    /// Gets properties this version does not know about, kept so a save does not drop them.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; init; }
}
