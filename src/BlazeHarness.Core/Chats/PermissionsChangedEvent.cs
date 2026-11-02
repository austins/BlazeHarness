using BlazeHarness.Core.Permissions;

namespace BlazeHarness.Core.Chats;

/// <summary>
/// The user changed the chat's access level or approvals. It is recorded for the history and does not appear in the conversation.
/// </summary>
/// <param name="At">When the settings changed.</param>
/// <param name="Access">The access level from then on.</param>
/// <param name="Approvals">The approval mode from then on.</param>
internal sealed record PermissionsChangedEvent(DateTimeOffset At, AccessLevel Access, ApprovalMode Approvals) : ChatEvent(At);
