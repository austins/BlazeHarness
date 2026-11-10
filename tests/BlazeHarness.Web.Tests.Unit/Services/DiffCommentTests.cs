using BlazeHarness.Core.Diff;
using BlazeHarness.Core.Git;
using BlazeHarness.Web.Services;

namespace BlazeHarness.Web.Tests.Unit.Services;

public sealed class DiffCommentTests
{
    [Fact]
    public void ToMessage_AddedLine_QuotesFileLineCodeAndText()
    {
        // Arrange
        var sut = new DiffComment("src/a.cs", [new GitDiffLine(DiffKind.Added, null, 12, "var x = 1;")], "Use a constant.");

        // Act
        var text = sut.ToMessage();

        // Assert
        text.Should().Be("`src/a.cs`, line 12:\n> var x = 1;\n\nUse a constant.");
    }

    [Fact]
    public void ToMessage_RemovedLine_NumbersTheOldVersion()
    {
        // Arrange
        var sut = new DiffComment("src/b.cs", [new GitDiffLine(DiffKind.Removed, 3, null, "old();")], "Why was this removed?");

        // Act
        var text = sut.ToMessage();

        // Assert
        text.Should().Be("`src/b.cs`, removed line 3:\n> old();\n\nWhy was this removed?");
    }

    [Fact]
    public void ToMessage_RangeOfOneKind_QuotesEveryLine()
    {
        // Arrange
        var sut = new DiffComment(
            "a.md",
            [new GitDiffLine(DiffKind.Added, null, 15, "## Title"), new GitDiffLine(DiffKind.Added, null, 16, "Text")],
            "Shorter?");

        // Act
        var text = sut.ToMessage();

        // Assert
        text.Should().Be("`a.md`, lines 15–16:\n> ## Title\n> Text\n\nShorter?");
    }

    [Fact]
    public void ToMessage_MixedRange_MarksEachLineAndNumbersTheNewVersion()
    {
        // Arrange
        var sut = new DiffComment(
            "a.md",
            [
                new GitDiffLine(DiffKind.Same, 23, 29, "---"),
                new GitDiffLine(DiffKind.Removed, 24, null, "Thank you"),
                new GitDiffLine(DiffKind.Added, null, 30, "Thanks"),
            ],
            "Keep it?");

        // Act
        var text = sut.ToMessage();

        // Assert
        text.Should().Be("`a.md`, lines 29–30:\n>   ---\n> - Thank you\n> + Thanks\n\nKeep it?");
    }

    [Fact]
    public void ToMessage_MoreLinesThanItQuotes_QuotesTheFirstAndKeepsTheWholeRange()
    {
        // Arrange
        var lines = Enumerable.Range(10, DiffComment.MaxQuotedLines + 2)
            .Select(n => new GitDiffLine(DiffKind.Added, null, n, $"line {n}"))
            .ToList();
        var sut = new DiffComment("a.cs", lines, "Split this.");

        // Act
        var text = sut.ToMessage();

        // Assert
        text.Should().Be($"`a.cs`, lines 10–{lines[^1].NewNumber}:\n> line 10\n> line 11\n> line 12\n> …\n\nSplit this.");
    }
}
