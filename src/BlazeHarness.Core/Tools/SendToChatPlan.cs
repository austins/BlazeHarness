using BlazeHarness.Core.Chats;
using System.Globalization;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// Where one <c>send_to_chat</c> call sends its message, worked out before anything happens.
/// </summary>
/// <param name="ChatId">The id of the receiving chat, or null when the call creates a new one.</param>
/// <param name="Title">The title of the receiving chat, or null for a new one.</param>
/// <param name="FolderPath">The folder the receiving chat works in, or null for a general chat.</param>
/// <param name="SameScope">True when the receiving chat has the same folder as the sending chat (or both have none) and no broader permissions or MCP tools.</param>
/// <param name="Message">What the sending agent wrote.</param>
internal sealed record SendToChatPlan(string? ChatId, string? Title, string? FolderPath, bool SameScope, string Message)
{
    /// <summary>
    /// The longest message a call may send, so the user can read all of it when asked.
    /// </summary>
    public const int MaxMessageLength = 6000;

    /// <summary>
    /// Gets a value indicating whether the call creates the receiving chat.
    /// </summary>
    public bool Creates => ChatId is null;

    /// <summary>
    /// Gets the text that asks the user to allow the call.
    /// </summary>
    /// <remarks>
    /// It names the receiving chat by id, so a call that later resolves to another chat with the same title is not the one the user approved.
    /// The message itself is left out: the approval shows the call's arguments, which hold it.
    /// </remarks>
    public string Reason
    {
        get
        {
            var where = FolderPath is null ? "no folder" : $"the folder '{FolderPath}'";
            var target = Creates ? $"a new chat in {where}" : $"the chat \"{Title}\" ({ChatReference.Format(ChatId!)}, {where})";
            return string.Create(CultureInfo.InvariantCulture, $"Sends a message to {target} and starts a turn there, which runs with that chat's own permissions.");
        }
    }
}
