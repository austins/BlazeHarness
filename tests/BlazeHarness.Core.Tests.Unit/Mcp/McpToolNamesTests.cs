using BlazeHarness.Core.Mcp;

namespace BlazeHarness.Core.Tests.Unit.Mcp;

public sealed class McpToolNamesTests
{
    [Fact]
    public void Build_ServerAndTool_JoinsThemWithTheSeparator()
    {
        // Act
        var name = McpToolNames.Build("files", "read", new HashSet<string>());

        // Assert
        name.Should().Be("files__read");
    }

    [Theory]
    [InlineData("search docs", "^files__search_docs_[0-9A-F]{6}$")]
    [InlineData("a.b/c", "^files__a_b_c_[0-9A-F]{6}$")]
    [InlineData("naïve", "^files__na_ve_[0-9A-F]{6}$")]
    public void Build_UnsafeCharacters_AreReplacedAndHashed(string tool, string expected)
    {
        // Act
        var name = McpToolNames.Build("files", tool, new HashSet<string>());

        // Assert
        name.Should().MatchRegex(expected);
    }

    [Fact]
    public void Build_TooLong_IsShortenedWithAHashAndStaysUnique()
    {
        // Arrange
        var taken = new HashSet<string>();
        var first = new string('a', 80) + "1";
        var second = new string('a', 80) + "2";

        // Act
        var one = McpToolNames.Build("files", first, taken);
        var two = McpToolNames.Build("files", second, taken);

        // Assert
        one.Length.Should().Be(McpToolNames.MaxLength);
        two.Length.Should().Be(McpToolNames.MaxLength);
        one.Should().NotBe(two);
        one.Should().MatchRegex("^[A-Za-z0-9_-]+$");
    }

    [Fact]
    public void Build_SameNameAfterCleaning_IsDistinctWhateverTheOrder()
    {
        // Act
        var forward = new HashSet<string>();
        var one = McpToolNames.Build("files", "a b", forward);
        var two = McpToolNames.Build("files", "a.b", forward);
        var backward = new HashSet<string>();
        var twoAgain = McpToolNames.Build("files", "a.b", backward);
        var oneAgain = McpToolNames.Build("files", "a b", backward);

        // Assert
        one.Should().NotBe(two);
        oneAgain.Should().Be(one);
        twoAgain.Should().Be(two);
    }

    [Fact]
    public void Build_NameThatNeedsNoCleaning_IsKeptAsItIs()
    {
        // Act
        var name = McpToolNames.Build("files", "a_b", new HashSet<string>());

        // Assert
        name.Should().Be("files__a_b");
    }

    [Fact]
    public void Build_LongNameWithAServerAtTheLimit_KeepsTheSeparator()
    {
        // Arrange
        var server = new string('s', McpToolNames.MaxServerLength);

        // Act
        var name = McpToolNames.Build(server, new string('t', 100), new HashSet<string>());

        // Assert
        McpToolNames.TrySplit(name, out var found, out _).Should().BeTrue();
        found.Should().Be(server);
    }

    [Fact]
    public void Build_SuffixOnALongName_StaysWithinTheLimit()
    {
        // Arrange
        var taken = new HashSet<string>();
        var tool = new string('x', 100);
        McpToolNames.Build("files", tool, taken);

        // Act
        var again = McpToolNames.Build("files", tool, taken);

        // Assert
        again.Length.Should().BeLessThanOrEqualTo(McpToolNames.MaxLength);
        taken.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("files__read", "files", "read")]
    [InlineData("files__read__more", "files", "read__more")]
    [InlineData("my_server__x", "my_server", "x")]
    [InlineData("files___x", "files", "_x")]
    public void TrySplit_NamespacedName_FindsTheServer(string name, string server, string tool)
    {
        // Act
        var split = McpToolNames.TrySplit(name, out var foundServer, out var foundTool);

        // Assert
        split.Should().BeTrue();
        foundServer.Should().Be(server);
        foundTool.Should().Be(tool);
    }

    [Theory]
    [InlineData("read_file")]
    [InlineData("__x")]
    [InlineData("files__")]
    [InlineData("")]
    public void TrySplit_BuiltInName_HasNoServer(string name)
    {
        // Act
        var split = McpToolNames.TrySplit(name, out var server, out var tool);

        // Assert
        split.Should().BeFalse();
        server.Should().BeEmpty();
        tool.Should().Be(name);
    }
}
