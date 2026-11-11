using System.ClientModel.Primitives;

namespace BlazeHarness.Core.Tests.Integration.Providers;

/// <summary>
/// A retry policy that retries at once, so tests do not wait for the real backoff.
/// </summary>
/// <param name="maxRetries">The number of times a failed request is retried.</param>
internal sealed class NoDelayRetryPolicy(int maxRetries) : ClientRetryPolicy(maxRetries)
{
    protected override TimeSpan GetNextDelay(PipelineMessage message, int tryCount) => TimeSpan.Zero;
}
