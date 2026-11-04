using BlazeHarness.Core.Git;

namespace BlazeHarness.Core.Tests.Unit.Git;

public sealed class GitStatusParserTests
{
    [Fact]
    public void Parse_MixedEntries_ReadsPathAndBothLetters()
    {
        // Arrange
        var output = " M a.txt\0A  b/c.cs\0AM d.txt\0?? e f.txt\0";

        // Act
        var result = GitStatusParser.Parse(output);

        // Assert
        result.Should().Equal(
            new GitStatusEntry("a.txt", ' ', 'M'),
            new GitStatusEntry("b/c.cs", 'A', ' '),
            new GitStatusEntry("d.txt", 'A', 'M'),
            new GitStatusEntry("e f.txt", '?', '?'));
    }

    [Fact]
    public void Parse_IgnoredAndMalformedEntries_AreSkipped()
    {
        // Act
        var result = GitStatusParser.Parse("!! ignored.log\0xx\0 M ok.txt\0");

        // Assert
        result.Should().ContainSingle().Which.Path.Should().Be("ok.txt");
    }

    [Theory]
    [InlineData(' ', 'M', false, true, false)]
    [InlineData('M', ' ', true, false, false)]
    [InlineData('M', 'M', true, true, false)]
    [InlineData('?', '?', false, true, false)]
    [InlineData('U', 'U', false, true, true)]
    [InlineData('A', 'A', false, true, true)]
    [InlineData('D', 'D', false, true, true)]
    public void Entry_Letters_SetScopeFlags(char index, char workTree, bool staged, bool unstaged, bool conflicted)
    {
        // Act
        var entry = new GitStatusEntry("x", index, workTree);

        // Assert
        entry.IsStaged.Should().Be(staged);
        entry.IsUnstaged.Should().Be(unstaged);
        entry.IsConflicted.Should().Be(conflicted);
    }
}
