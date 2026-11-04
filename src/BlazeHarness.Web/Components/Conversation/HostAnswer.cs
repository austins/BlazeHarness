using BlazeHarness.Core.Permissions;

namespace BlazeHarness.Web.Components.Conversation;

/// <summary>
/// How the user answered a question about a host a running command wants to connect to.
/// </summary>
/// <param name="CallId">The id of the shell call.</param>
/// <param name="Host">The host that was asked about.</param>
/// <param name="Choice">Whether the command may connect, and whether that holds for the rest of the chat.</param>
/// <param name="Always">True when the user also wants a rule that allows the host from now on.</param>
public sealed record HostAnswer(string CallId, string Host, ApprovalChoice Choice, bool Always = false);
