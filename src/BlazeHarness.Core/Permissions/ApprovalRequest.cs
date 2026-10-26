namespace BlazeHarness.Core.Permissions;

/// <summary>
/// A tool call that waits for the user's answer.
/// </summary>
/// <param name="CallId">The provider's identifier for the call.</param>
/// <param name="ToolName">The name of the tool.</param>
/// <param name="ArgumentsJson">The call arguments as a JSON object string.</param>
/// <param name="Reason">What the user is asked to allow, in a sentence.</param>
/// <param name="CanRemember">True when <see cref="ApprovalChoice.AllowForSession"/> is offered.</param>
public sealed record ApprovalRequest(string CallId, string ToolName, string ArgumentsJson, string Reason, bool CanRemember);
