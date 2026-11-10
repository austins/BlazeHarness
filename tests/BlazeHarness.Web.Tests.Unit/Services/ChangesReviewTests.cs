using BlazeHarness.Core.Diff;
using BlazeHarness.Core.Git;
using BlazeHarness.Web.Services;

namespace BlazeHarness.Web.Tests.Unit.Services;

public sealed class ChangesReviewTests
{
    private readonly ChangesReview _sut = new();

    [Fact]
    public void IsViewed_FileChangedSinceMarked_ReturnsFalse()
    {
        // Arrange
        _sut.SetViewed(File("a.cs", "1"), viewed: true);

        // Act
        var unchanged = _sut.IsViewed(File("a.cs", "1"));
        var changed = _sut.IsViewed(File("a.cs", "2"));

        // Assert
        unchanged.Should().BeTrue();
        changed.Should().BeFalse();
    }

    [Fact]
    public void SetViewed_False_ClearsTheMark()
    {
        // Arrange
        var file = File("a.cs", "1");
        _sut.SetViewed(file, viewed: true);

        // Act
        _sut.SetViewed(file, viewed: false);

        // Assert
        _sut.IsViewed(file).Should().BeFalse();
    }

    [Fact]
    public void IsUpdated_FileNeverShown_ReturnsFalse()
    {
        // Act
        var updated = _sut.IsUpdated(File("a.cs", "1"));

        // Assert
        updated.Should().BeFalse();
    }

    [Fact]
    public void IsUpdated_FileChangedAfterItWasShown_ReturnsTrueUntilShownAgain()
    {
        // Arrange
        _sut.Look(File("a.cs", "1"));

        // Act
        var before = _sut.IsUpdated(File("a.cs", "1"));
        var after = _sut.IsUpdated(File("a.cs", "2"));
        _sut.Look(File("a.cs", "2"));
        var seen = _sut.IsUpdated(File("a.cs", "2"));

        // Assert
        before.Should().BeFalse();
        after.Should().BeTrue();
        seen.Should().BeFalse();
    }

    [Fact]
    public void ContextLines_WholeFile_AsksForTheWholeFile()
    {
        // Arrange
        _sut.Context += ChangesReview.ContextStep;

        // Act
        var expanded = _sut.ContextLines;
        _sut.WholeFile = true;
        var whole = _sut.ContextLines;
        _sut.ResetContext();

        // Assert
        expanded.Should().Be(ChangesReview.DefaultContext + ChangesReview.ContextStep);
        whole.Should().Be(GitChangesReader.WholeFileContext);
        _sut.ContextLines.Should().Be(ChangesReview.DefaultContext);
    }

    [Fact]
    public void Look_NewChanges_MarksTheFileViewedUntilItChanges()
    {
        // Act
        _sut.Look(File("a.cs", "1"));

        // Assert
        _sut.IsViewed(File("a.cs", "1")).Should().BeTrue();
        _sut.IsViewed(File("a.cs", "2")).Should().BeFalse();
        _sut.IsUpdated(File("a.cs", "2")).Should().BeTrue();
    }

    [Fact]
    public void SetViewed_FalseAfterLook_ClearsTheMark()
    {
        // Arrange
        var file = File("a.cs", "1");
        _sut.Look(file);

        // Act
        _sut.SetViewed(file, viewed: false);

        // Assert
        _sut.IsViewed(file).Should().BeFalse();
    }

    [Fact]
    public void CanShowMore_BlockAtTheStartWithShortTrailingContext_ReturnsFalse()
    {
        // Arrange
        var hunk = Hunk(firstLine: 1, leading: 0, trailing: 2);

        // Act
        var more = ChangesReview.CanShowMore(hunk, ChangesReview.DefaultContext);

        // Assert
        more.Should().BeFalse();
    }

    [Fact]
    public void CanShowMore_FullLeadingContextMidFile_ReturnsTrue()
    {
        // Arrange
        var hunk = Hunk(firstLine: 10, leading: 3, trailing: 1);

        // Act
        var more = ChangesReview.CanShowMore(hunk, ChangesReview.DefaultContext);

        // Assert
        more.Should().BeTrue();
    }

    [Fact]
    public void CanShowMore_FullTrailingContext_ReturnsTrue()
    {
        // Arrange
        var hunk = Hunk(firstLine: 1, leading: 0, trailing: 3);

        // Act
        var more = ChangesReview.CanShowMore(hunk, ChangesReview.DefaultContext);

        // Assert
        more.Should().BeTrue();
    }

    [Fact]
    public void CanShowMore_TruncatedDiff_ReturnsFalse()
    {
        // Arrange
        var diff = new GitFileDiff("a.cs", IsBinary: false, IsTruncated: true, [Hunk(firstLine: 10, leading: 3, trailing: 3)]);

        // Act
        var more = ChangesReview.CanShowMore(diff, ChangesReview.DefaultContext);

        // Assert
        more.Should().BeFalse();
    }

    private static GitChangedFile File(string path, string stamp) =>
        new(path, GitChangeKind.Modified, false, true, 1, 1, false, stamp);

    private static GitDiffHunk Hunk(int firstLine, int leading, int trailing)
    {
        List<GitDiffLine> lines = [];
        var number = firstLine;
        for (var i = 0; i < leading; i++, number++)
        {
            lines.Add(new GitDiffLine(DiffKind.Same, number, number, "same"));
        }

        lines.Add(new GitDiffLine(DiffKind.Added, null, number, "added"));
        number++;
        for (var i = 0; i < trailing; i++, number++)
        {
            lines.Add(new GitDiffLine(DiffKind.Same, number - 1, number, "same"));
        }

        return new GitDiffHunk("@@", lines);
    }
}
