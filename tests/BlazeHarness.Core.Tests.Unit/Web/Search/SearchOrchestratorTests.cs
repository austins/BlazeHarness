using BlazeHarness.Core.Web;
using BlazeHarness.Core.Web.Search;
using BlazeHarness.Core.Web.Search.Providers;
using BlazeHarness.Tests.Shared;
using Microsoft.Extensions.Time.Testing;

namespace BlazeHarness.Core.Tests.Unit.Web.Search;

public sealed class SearchOrchestratorTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly LoggerFake<SearchOrchestrator> _log = new();

    [Fact]
    public async Task SearchAsync_OneProviderThrowsUnexpectedly_KeepsOtherResults()
    {
        // Arrange
        var good = new FakeProvider(WebProviders.Exa, () => [new SearchResult("T", "https://a.com/", "s")]);
        var bad = new FakeProvider(WebProviders.DuckDuckGo, () => throw new InvalidOperationException());
        var sut = new SearchOrchestrator([bad, good], _time, _log);
        var settings = new WebSettings { Providers = [new WebProviderSetting(WebProviders.DuckDuckGo), new WebProviderSetting(WebProviders.Exa)] };

        // Act
        var outcome = await sut.SearchAsync("q", 5, settings, TestContext.Current.CancellationToken);

        // Assert
        outcome.Results.Should().ContainSingle();
        outcome.Reports.Single(r => r.Id == WebProviders.DuckDuckGo).Failure.Should().Be(SearchFailure.BadResponse);
    }

    [Fact]
    public async Task SearchAsync_Sequential_StopsAtFirstProviderWithResults()
    {
        // Arrange
        var first = new FakeProvider(WebProviders.DuckDuckGo, () => [new SearchResult("T", "https://a.com/", "s")]);
        var second = new FakeProvider(WebProviders.Exa, () => throw new InvalidOperationException());
        var sut = new SearchOrchestrator([first, second], _time, _log);
        var settings = new WebSettings
        {
            SearchMode = WebSearchMode.Sequential,
            Providers = [new WebProviderSetting(WebProviders.DuckDuckGo), new WebProviderSetting(WebProviders.Exa)],
        };

        // Act
        var outcome = await sut.SearchAsync("q", 5, settings, TestContext.Current.CancellationToken);

        // Assert
        outcome.Reports.Should().ContainSingle();
        second.Calls.Should().Be(0);
    }

    private sealed class FakeProvider(string id, Func<IReadOnlyList<SearchResult>> results) : ISearchProvider
    {
        public int Calls { get; private set; }

        public string Id => id;

        public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(results());
        }
    }
}
