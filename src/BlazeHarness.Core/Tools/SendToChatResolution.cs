namespace BlazeHarness.Core.Tools;

/// <summary>
/// What looking up the receiving chat of a <c>send_to_chat</c> call found.
/// </summary>
/// <param name="Plan">Where the message goes, or null when it can't go anywhere.</param>
/// <param name="Error">For a null plan, what the model is told.</param>
internal sealed record SendToChatResolution(SendToChatPlan? Plan, string? Error = null);
