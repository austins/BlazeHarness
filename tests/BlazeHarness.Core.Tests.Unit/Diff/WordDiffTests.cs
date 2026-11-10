using BlazeHarness.Core.Diff;

namespace BlazeHarness.Core.Tests.Unit.Diff;

public sealed class WordDiffTests
{
    [Fact]
    public void Compare_SingleWordChanged_MarksThatWordOnBothSides()
    {
        // Act
        var (removed, added) = WordDiff.Compare("let total = count + 1;", "let total = amount + 1;");

        // Assert
        removed.Should().Equal(new TextRange(12, 5));
        added.Should().Equal(new TextRange(12, 6));
    }

    [Fact]
    public void Compare_InsertedWord_MarksItOnlyInTheNewText()
    {
        // Act
        var (removed, added) = WordDiff.Compare("call(first, second)", "call(first, middle, second)");

        // Assert
        removed.Should().BeEmpty();
        added.Should().Equal(new TextRange(12, 8));
    }

    [Fact]
    public void Compare_IdenticalLines_ReturnsNoRanges()
    {
        // Act
        var (removed, added) = WordDiff.Compare("same line", "same line");

        // Assert
        removed.Should().BeEmpty();
        added.Should().BeEmpty();
    }

    [Fact]
    public void Compare_TotallyDifferentLines_ReturnsNoRanges()
    {
        // Act
        var (removed, added) = WordDiff.Compare("alpha beta", "gamma delta");

        // Assert
        removed.Should().BeEmpty();
        added.Should().BeEmpty();
    }

    [Fact]
    public void Compare_EmptyOldText_ReturnsNoRanges()
    {
        // Act
        var (removed, added) = WordDiff.Compare(string.Empty, "something new");

        // Assert
        removed.Should().BeEmpty();
        added.Should().BeEmpty();
    }

    [Fact]
    public void Compare_WhitespaceOnlyChange_MarksTheWhitespace()
    {
        // Act
        var (removed, added) = WordDiff.Compare("if (a  &&  b) { run(); }", "if (a && b) { run(); }");

        // Assert
        removed.Should().Equal(new TextRange(5, 2), new TextRange(9, 2));
        added.Should().Equal(new TextRange(5, 1), new TextRange(8, 1));
    }

    [Fact]
    public void Compare_NeighbouringChangedTokens_AreMergedIntoOneRange()
    {
        // Act
        var (removed, added) = WordDiff.Compare("keep one_two() keep keep keep", "keep three_four[] keep keep keep");

        // Assert
        removed.Should().Equal(new TextRange(5, 9));
        added.Should().Equal(new TextRange(5, 12));
    }

    [Fact]
    public void Compare_RangesOfSeveralChanges_AreAscendingAndDoNotOverlap()
    {
        // Act
        var (removed, added) = WordDiff.Compare("aaa bbb ccc ddd eee fff ggg hhh", "aaa BBB ccc ddd EEE fff ggg HHH");

        // Assert
        removed.Should().Equal(new TextRange(4, 3), new TextRange(16, 3), new TextRange(28, 3));
        added.Should().Equal(new TextRange(4, 3), new TextRange(16, 3), new TextRange(28, 3));
    }

    [Fact]
    public void Compare_MostlyChangedLines_ReturnsNoRanges()
    {
        // Act
        var (removed, added) = WordDiff.Compare("aaaaaa b cccccc", "xxxxxx b yyyyyy");

        // Assert
        removed.Should().BeEmpty();
        added.Should().BeEmpty();
    }

    [Fact]
    public void Compare_TextLongerThanTheLimit_ReturnsNoRanges()
    {
        // Arrange
        var common = new string('x', 2000);

        // Act
        var (removed, added) = WordDiff.Compare(common + " a", common + " b");

        // Assert
        removed.Should().BeEmpty();
        added.Should().BeEmpty();
    }

    [Fact]
    public void Compare_TooManyTokens_ReturnsNoRanges()
    {
        // Arrange
        var oldText = new string('+', 1000) + "x";
        var newText = new string('+', 1000) + "y";

        // Act
        var (removed, added) = WordDiff.Compare(oldText, newText);

        // Assert
        removed.Should().BeEmpty();
        added.Should().BeEmpty();
    }

    [Fact]
    public void Compare_SurrogatePairChanged_KeepsThePairWhole()
    {
        // Act
        var (removed, added) = WordDiff.Compare("say \U0001F600 now please ok", "say \U0001F601 now please ok");

        // Assert
        removed.Should().Equal(new TextRange(4, 2));
        added.Should().Equal(new TextRange(4, 2));
    }
}
