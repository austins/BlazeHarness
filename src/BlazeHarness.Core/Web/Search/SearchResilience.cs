namespace BlazeHarness.Core.Web.Search;

/// <summary>
/// How patiently the search providers are called, which tests shorten.
/// </summary>
/// <param name="RequestTimeout">How long one request to a provider may take.</param>
/// <param name="RetryDelay">How long to wait before the one retry, when the provider did not say.</param>
/// <param name="MaxRetryDelay">The longest wait a provider's <c>Retry-After</c> may cause.</param>
/// <param name="BreakDuration">How long a provider is left alone after it failed repeatedly.</param>
/// <param name="MinimumThroughput">How many calls within a minute it takes to pause a provider.</param>
/// <param name="OverallTimeout">How long a one-at-a-time search may keep trying providers before it returns what it has.</param>
internal sealed record SearchResilience(TimeSpan RequestTimeout, TimeSpan RetryDelay, TimeSpan MaxRetryDelay, TimeSpan BreakDuration, int MinimumThroughput, TimeSpan OverallTimeout)
{
    /// <summary>
    /// Gets the values used by the app.
    /// </summary>
    public static SearchResilience Default { get; } = new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1), 3, TimeSpan.FromSeconds(45));
}
