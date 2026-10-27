using BlazeHarness.Core.Diff;

namespace BlazeHarness.Core.Tests.Unit.Diff;

public sealed class LineDiffTests
{
    [Fact]
    public void Compare_SameText_ReturnsOnlySharedLines()
    {
        // Act
        var lines = LineDiff.Compare("a\nb", "a\nb");

        // Assert
        Render(lines).Should().Be(" a| b");
    }

    [Fact]
    public void Compare_ChangedMiddleLine_MarksOneRemovedAndOneAdded()
    {
        // Act
        var lines = LineDiff.Compare("a\nb\nc", "a\nB\nc");

        // Assert
        Render(lines).Should().Be(" a|-b|+B| c");
    }

    [Fact]
    public void Compare_InsertedLines_MarksThemAdded()
    {
        // Act
        var lines = LineDiff.Compare("a\nd", "a\nb\nc\nd");

        // Assert
        Render(lines).Should().Be(" a|+b|+c| d");
    }

    [Fact]
    public void Compare_DeletedLines_MarksThemRemoved()
    {
        // Act
        var lines = LineDiff.Compare("a\nb\nc\nd", "a\nd");

        // Assert
        Render(lines).Should().Be(" a|-b|-c| d");
    }

    [Fact]
    public void Compare_InterleavedChanges_KeepsTheSharedLinesBetween()
    {
        // Act
        var lines = LineDiff.Compare("a\nx\nb\ny\nc", "a\nb\nz\nc");

        // Assert
        Render(lines).Should().Be(" a|-x| b|-y|+z| c");
    }

    [Fact]
    public void Compare_EmptyOldText_MarksEverythingAdded()
    {
        // Act
        var lines = LineDiff.Compare(string.Empty, "a\nb");

        // Assert
        Render(lines).Should().Be("+a|+b");
    }

    [Fact]
    public void Compare_EmptyNewText_MarksEverythingRemoved()
    {
        // Act
        var lines = LineDiff.Compare("a\nb", string.Empty);

        // Assert
        Render(lines).Should().Be("-a|-b");
    }

    [Fact]
    public void Compare_CrlfAgainstLf_TreatsLinesAsEqual()
    {
        // Act
        var lines = LineDiff.Compare("a\r\nb", "a\nb");

        // Assert
        Render(lines).Should().Be(" a| b");
    }

    [Fact]
    public void Compare_VeryLargeChange_FallsBackToRemovedThenAdded()
    {
        // Arrange
        var before = string.Join('\n', Enumerable.Range(0, 2500).Select(i => "old" + i));
        var after = string.Join('\n', Enumerable.Range(0, 2500).Select(i => "new" + i));

        // Act
        var lines = LineDiff.Compare(before, after);

        // Assert
        lines.Should().HaveCount(5000);
        lines.Take(2500).Should().OnlyContain(l => l.Kind == DiffKind.Removed);
        lines.Skip(2500).Should().OnlyContain(l => l.Kind == DiffKind.Added);
    }

    private static string Render(IReadOnlyList<DiffLine> lines) =>
        string.Join('|', lines.Select(l => Sign(l.Kind) + l.Text));

    private static string Sign(DiffKind kind) => kind switch
    {
        DiffKind.Added => "+",
        DiffKind.Removed => "-",
        _ => " ",
    };
}
