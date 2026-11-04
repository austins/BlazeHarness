using BlazeHarness.Core.Diff;
using BlazeHarness.Core.Git;

namespace BlazeHarness.Core.Tests.Unit.Git;

public sealed class GitDiffParserTests
{
    private const string Sample =
        "diff --git a/a.txt b/a.txt\nindex 111..222 100644\n--- a/a.txt\n+++ b/a.txt\n" +
        "@@ -1,3 +1,3 @@ void M()\n one\n-two\n+TWO\n three\n" +
        "@@ -10 +10,2 @@\n-ten\n+ten\n+eleven\n\\ No newline at end of file\n";

    [Fact]
    public void Parse_TwoHunks_NumbersTheLines()
    {
        // Act
        var result = GitDiffParser.Parse("a.txt", Sample, outputWasCut: false);

        // Assert
        result.IsBinary.Should().BeFalse();
        result.Hunks.Should().HaveCount(2);
        result.Hunks[0].Header.Should().Be("@@ -1,3 +1,3 @@ void M()");
        result.Hunks[0].Lines.Should().Equal(
            new GitDiffLine(DiffKind.Same, 1, 1, "one"),
            new GitDiffLine(DiffKind.Removed, 2, null, "two"),
            new GitDiffLine(DiffKind.Added, null, 2, "TWO"),
            new GitDiffLine(DiffKind.Same, 3, 3, "three"));
        result.Hunks[1].Lines.Should().Equal(
            new GitDiffLine(DiffKind.Removed, 10, null, "ten"),
            new GitDiffLine(DiffKind.Added, null, 10, "ten"),
            new GitDiffLine(DiffKind.Added, null, 11, "eleven"));
    }

    [Fact]
    public void Parse_ContentLineThatLooksLikeAFileHeader_IsKept()
    {
        // Arrange
        var text = "@@ -1,2 +1,1 @@\n--- not a header\n+keep\n";

        // Act
        var result = GitDiffParser.Parse("a.md", text, outputWasCut: false);

        // Assert
        result.Hunks.Single().Lines.Select(l => l.Text).Should().Equal("-- not a header", "keep");
    }

    [Fact]
    public void Parse_BinaryNotice_MarksBinary()
    {
        // Act
        var result = GitDiffParser.Parse("i.png", "diff --git a/i.png b/i.png\nBinary files a/i.png and b/i.png differ\n", outputWasCut: false);

        // Assert
        result.IsBinary.Should().BeTrue();
        result.Hunks.Should().BeEmpty();
    }

    [Fact]
    public void Parse_CarriageReturns_AreRemovedFromLines()
    {
        // Act
        var result = GitDiffParser.Parse("a.txt", "@@ -1 +1 @@\r\n-a\r\n+b\r\n", outputWasCut: false);

        // Assert
        result.Hunks.Single().Lines.Select(l => l.Text).Should().Equal("a", "b");
    }

    [Fact]
    public void Parse_MoreThanTheLimit_IsCutOff()
    {
        // Arrange
        var text = "@@ -0,0 +1,5000 @@\n" + string.Concat(Enumerable.Range(0, 5000).Select(i => $"+line {i}\n"));

        // Act
        var result = GitDiffParser.Parse("big.txt", text, outputWasCut: false);

        // Assert
        result.IsTruncated.Should().BeTrue();
        result.Hunks.Single().Lines.Should().HaveCount(GitDiffParser.MaxLines);
    }

    [Fact]
    public void Parse_OutputAlreadyCut_ReportsTruncated()
    {
        // Act
        var result = GitDiffParser.Parse("a.txt", "@@ -1 +1 @@\n-a\n+b\n", outputWasCut: true);

        // Assert
        result.IsTruncated.Should().BeTrue();
    }
}
