using BlazeHarness.Core.Web.Search.Providers;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using System.Collections.Concurrent;

namespace BlazeHarness.Core.Web.Search;

/// <summary>
/// Runs a search over the enabled providers, in parallel or one after another, and merges what they find.
/// </summary>
/// <remarks>
/// Each provider has its own retry, circuit breaker and timeout, which outlive a single search: a provider that keeps failing or says to slow down
/// is left alone for a while, and its searches are skipped instead of waiting.
/// </remarks>
internal sealed partial class SearchOrchestrator
{
    private readonly List<Slot> _slots;
    private readonly ConcurrentDictionary<string, WebProviderStatus> _status = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly TimeSpan _overallTimeout;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchOrchestrator"/> class.
    /// </summary>
    /// <param name="providers">The providers it can use.</param>
    /// <param name="time">Times the waits and the status stamps.</param>
    /// <param name="log">The log, which gets provider ids, counts and failure kinds only.</param>
    /// <param name="resilience">How patiently providers are called; the app's values when null.</param>
    public SearchOrchestrator(IEnumerable<ISearchProvider> providers, TimeProvider time, ILogger log, SearchResilience? resilience = null)
    {
        _time = time;
        _log = log;
        _overallTimeout = (resilience ?? SearchResilience.Default).OverallTimeout;
        var options = resilience ?? SearchResilience.Default;
        _slots = [.. providers.Select(provider => new Slot(provider, BuildPipeline(options, time)))];
    }

    /// <summary>
    /// Get how a provider did the last time it was used.
    /// </summary>
    /// <param name="id">The provider id.</param>
    /// <returns>The status, or null when the provider has not been used yet.</returns>
    public WebProviderStatus? StatusOf(string id) => _status.GetValueOrDefault(id);

    /// <summary>
    /// Search with the enabled providers.
    /// </summary>
    /// <param name="query">The search text.</param>
    /// <param name="maxResults">The most results to return.</param>
    /// <param name="settings">Which providers to use and how.</param>
    /// <param name="cancellationToken">A token that stops the search.</param>
    /// <returns>The merged results and a report for each provider.</returns>
    public async Task<SearchOutcome> SearchAsync(string query, int maxResults, WebSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var selected = settings.ResolveProviders()
            .Where(setting => setting.Enabled)
            .Select(setting => _slots.Find(slot => slot.Provider.Id == setting.Id))
            .OfType<Slot>()
            .ToList();

        List<(string Provider, IReadOnlyList<SearchResult> Results)> found = [];
        List<ProviderReport> reports = [];
        if (settings.SearchMode == WebSearchMode.Sequential)
        {
            using var deadline = new CancellationTokenSource(_overallTimeout, _time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            foreach (var slot in selected)
            {
                (ProviderReport Report, IReadOnlyList<SearchResult> Results) run;
                try
                {
                    run = await RunAsync(slot, query, maxResults, linked.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Out of time: return what the earlier providers found.
                    break;
                }

                var (report, results) = run;
                reports.Add(report);
                found.Add((slot.Provider.Id, results));
                if (results.Count > 0)
                {
                    break;
                }
            }
        }
        else
        {
            var runs = await Task.WhenAll(selected.Select(slot => RunAsync(slot, query, maxResults, cancellationToken)));
            for (var i = 0; i < runs.Length; i++)
            {
                reports.Add(runs[i].Report);
                found.Add((selected[i].Provider.Id, runs[i].Results));
            }
        }

        return new SearchOutcome(RankFusion.Merge(found, maxResults), reports);
    }

    private static ResiliencePipeline BuildPipeline(SearchResilience options, TimeProvider time)
    {
        return new ResiliencePipelineBuilder { TimeProvider = time }
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = options.RetryDelay,
                BackoffType = DelayBackoffType.Constant,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder().Handle<SearchProviderException>(ex => ex.Failure is SearchFailure.RateLimited or SearchFailure.Unavailable),
                DelayGenerator = args => ValueTask.FromResult<TimeSpan?>(
                    args.Outcome.Exception is SearchProviderException { RetryAfter: { } wait } ? Min(wait, options.MaxRetryDelay) : null),
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.8,
                MinimumThroughput = options.MinimumThroughput,
                SamplingDuration = TimeSpan.FromMinutes(1),
                BreakDuration = options.BreakDuration,
                ShouldHandle = new PredicateBuilder().Handle<SearchProviderException>().Handle<TimeoutRejectedException>(),
            })
            .AddTimeout(options.RequestTimeout)
            .Build();
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private async Task<(ProviderReport Report, IReadOnlyList<SearchResult> Results)> RunAsync(Slot slot, string query, int maxResults, CancellationToken cancellationToken)
    {
        var id = slot.Provider.Id;
        try
        {
            var results = await slot.Pipeline.ExecuteAsync(
                async token => await slot.Provider.SearchAsync(query, maxResults, token),
                cancellationToken);
            Record(id, working: true, results.Count == 0 ? "The last search found nothing." : "Working.");
            LogSearched(id, results.Count);
            return (new ProviderReport(id, results.Count), results);
        }
        catch (BrokenCircuitException)
        {
            Record(id, working: false, "Paused for a minute after repeated failures.");
            LogPaused(id);
            return (new ProviderReport(id, 0, SearchFailure.Unavailable, Paused: true), []);
        }
        catch (TimeoutRejectedException)
        {
            return Failed(slot, SearchFailure.Unavailable);
        }
        catch (SearchProviderException ex)
        {
            return Failed(slot, ex.Failure);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A provider's reply had a shape its parser did not expect; it must not take the other providers' results with it.
            return Failed(slot, SearchFailure.BadResponse);
        }
    }

    private (ProviderReport Report, IReadOnlyList<SearchResult> Results) Failed(Slot slot, SearchFailure failure)
    {
        var id = slot.Provider.Id;
        Record(id, working: false, failure switch
        {
            SearchFailure.RateLimited => "The provider said to slow down.",
            SearchFailure.Blocked => "The provider refused the request.",
            SearchFailure.BadResponse => "The provider's answer could not be read.",
            _ => "The provider could not be reached.",
        });
        LogFailed(id, failure);
        return (new ProviderReport(id, 0, failure), []);
    }

    private void Record(string id, bool working, string detail)
    {
        _status[id] = new WebProviderStatus(id, working, detail, _time.GetUtcNow());
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Search provider {Provider} returned {Count} results.")]
    private partial void LogSearched(string provider, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Search provider {Provider} failed: {Failure}.")]
    private partial void LogFailed(string provider, SearchFailure failure);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Search provider {Provider} is paused after repeated failures.")]
    private partial void LogPaused(string provider);

    private sealed record Slot(ISearchProvider Provider, ResiliencePipeline Pipeline);
}
