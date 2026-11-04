using BlazeHarness.Core.Git;

namespace BlazeHarness.Core.Tests.Unit.Git;

public sealed class GitNumstatParserTests
{
    [Fact]
    public void Parse_TextAndBinaryFiles_ReadsCounts()
    {
        // Act
        var result = GitNumstatParser.Parse("3\t1\tsrc/a.cs\0-\t-\timg.png\0");

        // Assert
        result["src/a.cs"].Should().Be((3, 1));
        result["img.png"].Should().Be((null, null));
    }

    [Fact]
    public void Parse_Empty_ReturnsNothing()
    {
        // Act
        var result = GitNumstatParser.Parse(string.Empty);

        // Assert
        result.Should().BeEmpty();
    }
}
