using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Tests.Unit.Tools;

public sealed class GitIgnoreTests
{
    [Theory]
    [InlineData("bin/", "bin", true, true)]
    [InlineData("bin/", "bin", false, null)]
    [InlineData("bin/", "src/bin", true, true)]
    [InlineData("*.log", "a/b/run.log", false, true)]
    [InlineData("/build", "build", true, true)]
    [InlineData("/build", "src/build", true, null)]
    [InlineData("docs/*.md", "docs/a.md", false, true)]
    [InlineData("docs/*.md", "other/docs/a.md", false, null)]
    [InlineData("# comment", "# comment", false, null)]
    [InlineData("\\#file", "#file", false, true)]
    public void Check_SingleRule_GivesTheVerdict(string rule, string path, bool isDirectory, bool? expected)
    {
        // Arrange
        var sut = GitIgnore.Parse(rule);

        // Act
        var result = sut.Check(path, isDirectory);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Check_LaterNegation_BringsTheFileBack()
    {
        // Arrange
        var sut = GitIgnore.Parse("*.log\n!keep.log\n");

        // Act
        var ignored = sut.Check("a.log", false);
        var kept = sut.Check("keep.log", false);

        // Assert
        ignored.Should().BeTrue();
        kept.Should().BeFalse();
    }

    [Fact]
    public void Parse_RuleWithAReversedRange_IsSkippedAndTheOthersStillApply()
    {
        // Arrange
        var sut = GitIgnore.Parse("[z-a]x\n*.log\n");

        // Act
        var bad = sut.Check("qx", false);
        var good = sut.Check("a.log", false);

        // Assert
        bad.Should().BeNull();
        good.Should().BeTrue();
    }

    [Fact]
    public void Check_ManyRepeatedDoubleStars_StaysFastAndMatches()
    {
        // Arrange
        var sut = GitIgnore.Parse(string.Concat(Enumerable.Repeat("**/", 40)) + "x.log\n");
        var path = string.Join('/', Enumerable.Repeat("dir", 200)) + "/y.log";

        // Act
        var other = sut.Check(path, false);
        var match = sut.Check(path.Replace("y.log", "x.log", StringComparison.Ordinal), false);

        // Assert
        other.Should().BeNull();
        match.Should().BeTrue();
    }

    [Fact]
    public void Parse_WindowsLineEndingsAndBlankLines_AreHandled()
    {
        // Arrange
        var sut = GitIgnore.Parse("\r\n*.tmp  \r\n\r\n");

        // Act
        var result = sut.Check("x.tmp", false);

        // Assert
        result.Should().BeTrue();
    }
}
