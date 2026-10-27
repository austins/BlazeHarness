using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Tests.Unit.Tools;

public sealed class ShellOutputTests
{
    private readonly ShellOutput _sut = new();

    [Fact]
    public void ToString_SmallOutput_ReturnsEveryLine()
    {
        // Act
        _sut.AppendLine("a");
        _sut.AppendLine("b");

        // Assert
        _sut.ToString().Should().Be("a\nb\n");
    }

    [Fact]
    public void ToString_OutputOverTheLimits_KeepsTheStartAndTheEndAndCountsWhatWasLeftOut()
    {
        // Arrange
        var total = 0;

        // Act
        for (var i = 0; i < 20_000; i++)
        {
            var line = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _sut.AppendLine(line);
            total += line.Length + 1;
        }

        // Assert
        var text = _sut.ToString();
        var omitted = total - ShellOutput.HeadLimit - ShellOutput.TailLimit;
        text.Should().StartWith("0\n1\n2\n").And.EndWith("19999\n").And.Contain($"[{omitted} characters left out]");
        text.Length.Should().BeLessThan(ToolSet.MaxOutputCharacters);
    }

    [Fact]
    public void ToString_SingleLineLongerThanTheLimits_StillStaysBounded()
    {
        // Act
        _sut.AppendLine(new string('x', 100_000));

        // Assert
        _sut.ToString().Length.Should().BeLessThan(ToolSet.MaxOutputCharacters);
    }

    [Fact]
    public void ToString_ManyShortLines_TailIsExactlyTheLastCharacters()
    {
        // Arrange
        var everything = new System.Text.StringBuilder();

        // Act
        for (var i = 0; i < 30_000; i++)
        {
            var line = "line " + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _sut.AppendLine(line);
            everything.Append(line).Append('\n');
        }

        // Assert
        var text = _sut.ToString();
        var all = everything.ToString();
        text.Should().StartWith(all[..ShellOutput.HeadLimit]).And.EndWith(all[^ShellOutput.TailLimit..]);
        text.Length.Should().BeLessThan(ShellOutput.HeadLimit + ShellOutput.TailLimit + 100);
    }
}
