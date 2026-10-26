namespace BlazeHarness.Core.Providers;

/// <summary>
/// The outcome of "Test connection".
/// </summary>
/// <param name="Success">Whether the endpoint answered and accepted the credentials.</param>
/// <param name="Message">A user-readable explanation, with a suggested fix when it failed.</param>
/// <param name="ModelIds">The model ids the endpoint listed; empty when it lists none or the test failed.</param>
public sealed record ConnectionTestResult(bool Success, string Message, IReadOnlyList<string> ModelIds);
