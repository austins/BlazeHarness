using BlazeHarness.Core.Chats.Titles;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Providers;
using System.Text.Json.Serialization;

namespace BlazeHarness.Core.Chats;

/// <summary>
/// The contents of a chat's <c>chat.json</c>: its title, times, folder and the settings it was last used with.
/// </summary>
public sealed record ChatInfo
{
    /// <summary>
    /// Gets the chat id, which is also the name of its folder.
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
    /// Gets when the chat was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Gets when the chat last had activity, which orders the chat list.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Gets the folder the chat works in, or null for a chat without one.
    /// </summary>
    public string? FolderPath { get; init; }

    /// <summary>
    /// Gets the id of the provider the chat last used.
    /// </summary>
    public string? ProviderId { get; init; }

    /// <summary>
    /// Gets the id of the model the chat last used.
    /// </summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// Gets the thinking level the chat last used, or null for the server default.
    /// </summary>
    public ThinkingLevel? Level { get; init; }

    /// <summary>
    /// Gets the access level the user chose for the chat, or null for the default.
    /// </summary>
    public AccessLevel? Access { get; init; }

    /// <summary>
    /// Gets the approval mode the user chose for the chat, or null for the default.
    /// </summary>
    public ApprovalMode? Approvals { get; init; }

    /// <summary>
    /// Gets the MCP servers this chat turns on or off against their global state, by server name, or null when none differ.
    /// </summary>
    public IReadOnlyDictionary<string, bool>? McpServers { get; init; }

    /// <summary>
    /// Gets the skills this chat turns on or off against their global state, by skill name, or null when none differ.
    /// </summary>
    public IReadOnlyDictionary<string, bool>? Skills { get; init; }

    /// <summary>
    /// Gets the permission settings the chat runs with.
    /// </summary>
    [JsonIgnore]
    public PermissionSettings Permissions => new(Access ?? PermissionSettings.Default.Access, Approvals ?? PermissionSettings.Default.Approvals);
}
