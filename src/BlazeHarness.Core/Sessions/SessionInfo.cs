using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Providers;
using System.Text.Json.Serialization;

namespace BlazeHarness.Core.Sessions;

/// <summary>
/// The contents of a session's <c>session.json</c>: its title, times, folder and the settings it was last used with.
/// </summary>
public sealed record SessionInfo
{
    /// <summary>
    /// Gets the session id, which is also the name of its folder.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets the title, or an empty string before the first message.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Gets where <see cref="Title"/> came from.
    /// </summary>
    public TitleSource TitleSource { get; init; }

    /// <summary>
    /// Gets when the session was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Gets when the session last had activity, which orders the session list.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Gets the folder the session works in, or null for a session without one.
    /// </summary>
    public string? FolderPath { get; init; }

    /// <summary>
    /// Gets the id of the provider the session last used.
    /// </summary>
    public string? ProviderId { get; init; }

    /// <summary>
    /// Gets the id of the model the session last used.
    /// </summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// Gets the thinking level the session last used, or null for the server default.
    /// </summary>
    public ThinkingLevel? Level { get; init; }

    /// <summary>
    /// Gets the access level the user chose for the session, or null for the default.
    /// </summary>
    public AccessLevel? Access { get; init; }

    /// <summary>
    /// Gets the approval mode the user chose for the session, or null for the default.
    /// </summary>
    public ApprovalMode? Approvals { get; init; }

    /// <summary>
    /// Gets the MCP servers this session turns on or off against their global state, by server name, or null when none differ.
    /// </summary>
    public IReadOnlyDictionary<string, bool>? McpServers { get; init; }

    /// <summary>
    /// Gets the skills this session turns on or off against their global state, by skill name, or null when none differ.
    /// </summary>
    public IReadOnlyDictionary<string, bool>? Skills { get; init; }

    /// <summary>
    /// Gets the permission settings the session runs with.
    /// </summary>
    [JsonIgnore]
    public PermissionSettings Permissions => new(Access ?? PermissionSettings.Default.Access, Approvals ?? PermissionSettings.Default.Approvals);
}
