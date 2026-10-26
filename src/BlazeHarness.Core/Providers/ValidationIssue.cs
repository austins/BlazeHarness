namespace BlazeHarness.Core.Providers;

/// <summary>
/// One problem found while validating provider settings.
/// </summary>
/// <param name="Path">Where the problem is, e.g. <c>baseUrl</c> or <c>models[1].maxOutputTokens</c>, so the UI can mark the right field.</param>
/// <param name="Message">A user-readable description of what to fix.</param>
public sealed record ValidationIssue(string Path, string Message);
