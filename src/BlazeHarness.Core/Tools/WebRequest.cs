namespace BlazeHarness.Core.Tools;

/// <summary>
/// What a web tool call is about, for the permission check.
/// </summary>
/// <param name="Target">The text that permission rules match: the host for a fetch, or <c>web_search</c>.</param>
/// <param name="GrantKey">The key an approval is remembered under for the session.</param>
/// <param name="Reason">What the user is asked to approve.</param>
internal sealed record WebRequest(string Target, string GrantKey, string Reason);
