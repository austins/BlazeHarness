using BlazeHarness.Core.Diff;
using BlazeHarness.Core.Git;

namespace BlazeHarness.Core.Tests.Unit.Git;

public sealed class GitDiffHunkTests
{
    [Fact]
    public void SplitChanges_TwoChangesBetweenContext_GivesEachItsOwnRange()
    {
        // Arrange
        var sut = new GitDiffHunk(
            "@@ -1,6 +1,6 @@",
            [
                new GitDiffLine(DiffKind.Same, 1, 1, "a"),
                new GitDiffLine(DiffKind.Removed, 2, null, "b"),
                new GitDiffLine(DiffKind.Added, null, 2, "B"),
                new GitDiffLine(DiffKind.Same, 3, 3, "c"),
                new GitDiffLine(DiffKind.Removed, 4, null, "d"),
                new GitDiffLine(DiffKind.Same, 5, 4, "e"),
                new GitDiffLine(DiffKind.Added, null, 5, "f"),
                new GitDiffLine(DiffKind.Same, 6, 6, "g"),
            ]);

        // Act
        var changes = sut.SplitChanges();

        // Assert
        changes.Select(c => (c.Start, c.Change.Header, c.Change.Lines.Count)).Should().Equal(
            (1, "@@ -2,1 +2,1 @@", 2),
            (4, "@@ -4,1 +3,0 @@", 1),
            (6, "@@ -5,0 +5,1 @@", 1));
    }

    [Fact]
    public void SplitChanges_AdditionAtTheStartOfAnEmptyFile_NumbersFromZero()
    {
        // Arrange
        var sut = new GitDiffHunk("@@ -0,0 +1,2 @@", [new GitDiffLine(DiffKind.Added, null, 1, "a"), new GitDiffLine(DiffKind.Added, null, 2, "b")]);

        // Act
        var changes = sut.SplitChanges();

        // Assert
        changes.Should().ContainSingle().Which.Change.Header.Should().Be("@@ -0,0 +1,2 @@");
    }

    [Fact]
    public void SplitChanges_UnreadableHeader_ReturnsNone()
    {
        // Arrange
        var sut = new GitDiffHunk("not a header", [new GitDiffLine(DiffKind.Added, null, 1, "a")]);

        // Act
        var changes = sut.SplitChanges();

        // Assert
        changes.Should().BeEmpty();
    }
}
