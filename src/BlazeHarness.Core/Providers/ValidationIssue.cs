namespace BlazeHarness.Core.Providers;

/// <summary>
/// One problem found while validating provider settings.
/// </summary>
/// <param name="Path">Where the problem is, e.g. <c>baseUrl</c> or <c>models[1].maxOutputTokens</c>, so the UI can mark the right field.</param>
/// <param name="Code">Which problem it is, so a UI can show it in the user's language.</param>
/// <param name="Message">An English description of what to fix, for logs and callers without a translation.</param>
/// <param name="Arguments">Values the description mentions, by the name a translated message uses. Includes examples a translation should not change.</param>
public sealed record ValidationIssue(
    string Path,
    ValidationCode Code,
    string Message,
    IReadOnlyDictionary<string, object?>? Arguments = null);
