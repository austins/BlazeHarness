using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Tests.Unit.Tools;

public sealed class CappedLineReaderTests
{
    [Fact]
    public void ReadLine_MixedLineEndings_SplitsLikeAStreamReader()
    {
        // Arrange
        var sut = new CappedLineReader(new StringReader("a\nb\r\nc\rd\n\ne"), 100);

        // Act
        var lines = ReadAll(sut);

        // Assert
        lines.Should().Equal("a", "b", "c", "d", string.Empty, "e");
    }

    [Fact]
    public void ReadLine_TrailingLineEnding_DoesNotAddAnEmptyLine()
    {
        // Arrange
        var sut = new CappedLineReader(new StringReader("a\n"), 100);

        // Act
        var lines = ReadAll(sut);

        // Assert
        lines.Should().Equal("a");
    }

    [Fact]
    public void ReadLine_EmptyText_ReturnsNull()
    {
        // Arrange
        var sut = new CappedLineReader(new StringReader(string.Empty), 100);

        // Act
        var line = sut.ReadLine();

        // Assert
        line.Should().BeNull();
    }

    [Fact]
    public void ReadLine_LineLongerThanTheCap_KeepsTheStartAndCountsWhatWasDropped()
    {
        // Arrange
        var sut = new CappedLineReader(new StringReader(new string('x', 10_000) + "\nnext\n"), 100);

        // Act
        var first = sut.ReadLine();
        var dropped = sut.DroppedCharacters;
        var second = sut.ReadLine();

        // Assert
        first.Should().Be(new string('x', 100));
        dropped.Should().Be(9_900);
        second.Should().Be("next");
        sut.DroppedCharacters.Should().Be(0);
    }

    [Fact]
    public void ReadLine_CarriageReturnAndLineFeedSplitAcrossReads_IsOneLineEnd()
    {
        // Arrange
        var sut = new CappedLineReader(new OneCharacterAtATimeReader("a\r\nb"), 100);

        // Act
        var lines = ReadAll(sut);

        // Assert
        lines.Should().Equal("a", "b");
    }

    [Fact]
    public async Task ReadLineAsync_MixedLineEndingsAndALongLine_MatchesTheSyncReader()
    {
        // Arrange
        var sut = new CappedLineReader(new StringReader("a\r\n" + new string('y', 5_000) + "\r\nlast"), 10);

        // Act
        var first = await sut.ReadLineAsync(TestContext.Current.CancellationToken);
        var second = await sut.ReadLineAsync(TestContext.Current.CancellationToken);
        var secondDropped = sut.DroppedCharacters;
        var third = await sut.ReadLineAsync(TestContext.Current.CancellationToken);
        var end = await sut.ReadLineAsync(TestContext.Current.CancellationToken);

        // Assert
        first.Should().Be("a");
        second.Should().Be(new string('y', 10));
        secondDropped.Should().Be(4_990);
        third.Should().Be("last");
        end.Should().BeNull();
    }

    private static List<string> ReadAll(CappedLineReader reader)
    {
        List<string> lines = [];
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    private sealed class OneCharacterAtATimeReader(string text) : TextReader
    {
        private int _position;

        public override int Read(char[] buffer, int index, int count)
        {
            if (_position >= text.Length)
            {
                return 0;
            }

            buffer[index] = text[_position++];
            return 1;
        }
    }
}
