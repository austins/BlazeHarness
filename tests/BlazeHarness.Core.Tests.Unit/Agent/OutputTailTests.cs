using BlazeHarness.Core.Agent;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class OutputTailTests
{
    private readonly OutputTail _sut = new();

    [Fact]
    public void Text_MoreLinesThanTheLimit_KeepsOnlyTheLastOnes()
    {
        // Act
        for (var i = 1; i <= OutputTail.MaxLines + 3; i++)
        {
            _sut.Append($"line {i}");
        }

        // Assert
        var lines = _sut.Text.Split('\n');
        lines.Should().HaveCount(OutputTail.MaxLines);
        lines[0].Should().Be("line 4");
        lines[^1].Should().Be($"line {OutputTail.MaxLines + 3}");
    }

    [Fact]
    public void Append_ProgressLineThatRedrawsItself_KeepsWhatATerminalWouldShow()
    {
        // Act
        _sut.Append("10%\r50%\r100%\r");

        // Assert
        _sut.Text.Should().Be("100%");
    }

    [Fact]
    public void Append_LongLine_IsCutWithAnEllipsis()
    {
        // Act
        _sut.Append(new string('x', OutputTail.MaxLineCharacters + 50));

        // Assert
        _sut.Text.Should().Be(new string('x', OutputTail.MaxLineCharacters) + "…");
    }

    [Fact]
    public void Text_NothingAppended_IsEmpty()
    {
        // Assert
        _sut.Text.Should().BeEmpty();
    }
}
