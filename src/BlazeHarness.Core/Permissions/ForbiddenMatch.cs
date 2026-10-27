namespace BlazeHarness.Core.Permissions;

/// <summary>
/// A command that the forbidden list flagged.
/// </summary>
/// <param name="Risk">A sentence that says what is risky about the command.</param>
public sealed record ForbiddenMatch(string Risk);
