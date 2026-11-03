using BlazeHarness.Core.Compaction;

namespace BlazeHarness.Core.Tests.Unit.Compaction;

public sealed class OutputStubTests
{
    [Fact]
    public void For_ALargeResult_IsTheStub()
    {
        // Arrange
        var result = Text(OutputStub.MinTokens);

        // Act
        var stub = OutputStub.For("read_file", result);

        // Assert
        stub.Should().Be(OutputStub.Text);
    }

    [Fact]
    public void For_ASmallResult_IsLeftAlone()
    {
        // Arrange
        var result = Text(OutputStub.MinTokens - 1);

        // Act
        var stub = OutputStub.For("read_file", result);

        // Assert
        stub.Should().BeNull();
    }

    [Fact]
    public void For_NoResult_IsLeftAlone()
    {
        // Arrange / Act
        var stub = OutputStub.For("read_file", null);

        // Assert
        stub.Should().BeNull();
    }

    [Theory]
    [InlineData("update_todos")]
    [InlineData("load_skill")]
    [InlineData("ask_user")]
    public void For_ToolsWhoseResultsMustStay_IsLeftAlone(string tool)
    {
        // Arrange
        var result = Text(1_000);

        // Act
        var stub = OutputStub.For(tool, result);

        // Assert
        stub.Should().BeNull();
    }

    [Fact]
    public void For_ALargeFailure_KeepsTheFirstLine()
    {
        // Arrange
        var result = "Error: could not read 'a.txt'\n" + Text(1_000);

        // Act
        var stub = OutputStub.For("read_file", result);

        // Assert
        stub.Should().Be("Error: could not read 'a.txt'\n" + OutputStub.Text);
    }

    [Fact]
    public void For_AFailureWithAVeryLongFirstLine_CutsTheLine()
    {
        // Arrange
        var result = "Error: " + Text(1_000);

        // Act
        var stub = OutputStub.For("shell", result);

        // Assert
        stub.Should().StartWith("Error: xxx").And.EndWith("…\n" + OutputStub.Text);
        stub.Length.Should().BeLessThan(OutputStub.Text.Length + 320);
    }

    [Fact]
    public void For_AResultThatIsAlreadyTheStub_IsLeftAlone()
    {
        // Arrange / Act
        var stub = OutputStub.For("shell", OutputStub.Text);

        // Assert
        stub.Should().BeNull();
    }

    private static string Text(int tokens) => new('x', tokens * TokenEstimate.CharactersPerToken);
}
