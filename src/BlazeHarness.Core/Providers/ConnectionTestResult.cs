namespace BlazeHarness.Core.Providers;

/// <summary>
/// The outcome of "Test connection".
/// </summary>
/// <param name="Success">Whether the endpoint answered and accepted the credentials.</param>
/// <param name="Code">Which outcome it is, so a UI can show it in the user's language.</param>
/// <param name="Message">An English explanation, with a suggested fix when it failed, for callers without a translation.</param>
/// <param name="ModelIds">The model ids the endpoint listed; empty when it lists none or the test failed.</param>
/// <param name="Arguments">Values the explanation mentions, by the name a translated message uses.</param>
public sealed record ConnectionTestResult(
    bool Success,
    ConnectionTestCode Code,
    string Message,
    IReadOnlyList<string> ModelIds,
    IReadOnlyDictionary<string, object?>? Arguments = null);
