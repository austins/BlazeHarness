using BlazeHarness.Core.Mcp;

namespace BlazeHarness.Core.Tests.Unit.Mcp;

public sealed class McpToolRankerTests
{
    private static readonly McpToolListing[] Tools =
    [
        Listing("github", "create_issue", "Create a new issue in a repository.", """{"type":"object","properties":{"title":{"type":"string"}}}"""),
        Listing("github", "searchPullRequests", "Find pull requests by author or label."),
        Listing("reddit", "get_posts", "Fetch posts from a subreddit."),
        Listing("docs", "microsoft_docs_search", "Search the official documentation."),
        Listing("cjk", "search_news", "查询最新新闻"),
    ];

    [Fact]
    public void Rank_QueryNamingTheWork_PutsTheMatchingToolFirst()
    {
        // Act
        var ranked = McpToolRanker.Rank(Tools, "open a new issue");

        // Assert
        ranked[0].Tool.Name.Should().Be("github__create_issue");
    }

    [Fact]
    public void Rank_CamelCaseName_IsSplitIntoWords()
    {
        // Act
        var ranked = McpToolRanker.Rank(Tools, "pull request");

        // Assert
        ranked[0].Tool.RemoteName.Should().Be("searchPullRequests");
    }

    [Fact]
    public void Rank_ExactToolName_BeatsEverythingElse()
    {
        // Act
        var ranked = McpToolRanker.Rank(Tools, "github__create_issue");

        // Assert
        ranked[0].Tool.Name.Should().Be("github__create_issue");
    }

    [Fact]
    public void Rank_QueryInTheDescriptionLanguage_MatchesByBigrams()
    {
        // Act
        var ranked = McpToolRanker.Rank(Tools, "新闻");

        // Assert
        ranked.Should().NotBeEmpty();
        ranked[0].Tool.RemoteName.Should().Be("search_news");
    }

    [Fact]
    public void Rank_NothingInCommon_ReturnsNoResults()
    {
        // Act
        var ranked = McpToolRanker.Rank(Tools, "quantum entanglement");

        // Assert
        ranked.Should().BeEmpty();
    }

    [Fact]
    public void Rank_OnlyStopWords_ReturnsNoResults()
    {
        // Act
        var ranked = McpToolRanker.Rank(Tools, "the of a");

        // Assert
        ranked.Should().BeEmpty();
    }

    [Fact]
    public void Tokenize_MixedText_SplitsCaseAndPunctuation()
    {
        // Act
        var words = McpToolRanker.Tokenize("getUserName-from_db");

        // Assert
        words.Should().Contain(["get", "user", "name", "db"]);
    }

    private static McpToolListing Listing(string server, string tool, string description, string schema = """{"type":"object","properties":{}}""")
        => new($"{server}__{tool}", server, tool, description, schema);
}
