using BlazeHarness.Core.Web.Search;

namespace BlazeHarness.Core.Tests.Unit.Web.Search;

public sealed class RankFusionTests
{
    [Fact]
    public void Merge_SameUrlFromTwoProviders_CombinesAndRanksFirst()
    {
        // Arrange
        var a = new SearchResult("A", "https://a.com/", "short");
        var b = new SearchResult("B", "https://b.com/", "b");
        var aAgain = new SearchResult("A", "https://www.a.com/?utm_source=x", "a longer snippet");

        // Act
        var merged = RankFusion.Merge([("p1", [b, a]), ("p2", [aAgain])], 10);

        // Assert
        merged.Should().HaveCount(2);
        merged[0].Providers.Should().HaveCount(2).And.Contain("p1").And.Contain("p2");
        merged[0].Result.Snippet.Should().Be("a longer snippet");
    }

    [Fact]
    public void Merge_MoreThanMax_Truncates()
    {
        // Arrange
        var results = Enumerable.Range(0, 5).Select(i => new SearchResult("t", $"https://x.com/{i}", string.Empty)).ToList();

        // Act
        var merged = RankFusion.Merge([("p", results)], 3);

        // Assert
        merged.Should().HaveCount(3);
    }
}
