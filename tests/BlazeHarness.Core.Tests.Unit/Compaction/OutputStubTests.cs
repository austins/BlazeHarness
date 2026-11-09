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
        var stub = OutputStub.For("read_file", "c1", result);

        // Assert
        stub.Should().Be(OutputStub.Text("c1"));
        stub.Should().StartWith(OutputStub.Marker).And.Contain("search_chat with ref \"c1\"");
    }

    [Fact]
    public void For_ASmallResult_IsLeftAlone()
    {
        // Arrange
        var result = Text(OutputStub.MinTokens - 1);

        // Act
        var stub = OutputStub.For("read_file", "c1", result);

        // Assert
        stub.Should().BeNull();
    }

    [Fact]
    public void For_NoResult_IsLeftAlone()
    {
        // Arrange / Act
        var stub = OutputStub.For("read_file", "c1", null);

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
        var stub = OutputStub.For(tool, "c1", result);

        // Assert
        stub.Should().BeNull();
    }

    [Fact]
    public void For_ALargeFailure_KeepsTheFirstLine()
    {
        // Arrange
        var result = "Error: could not read 'a.txt'\n" + Text(1_000);

        // Act
        var stub = OutputStub.For("read_file", "c1", result);

        // Assert
        stub.Should().Be("Error: could not read 'a.txt'\n" + OutputStub.Text("c1"));
    }

    [Fact]
    public void For_AFailureWithAVeryLongFirstLine_CutsTheLine()
    {
        // Arrange
        var result = "Error: " + Text(1_000);

        // Act
        var stub = OutputStub.For("shell", "c1", result);

        // Assert
        stub.Should().StartWith("Error: xxx").And.EndWith("…\n" + OutputStub.Text("c1"));
        stub.Length.Should().BeLessThan(OutputStub.Text("c1").Length + 320);
    }

    [Fact]
    public void For_AResultThatIsAlreadyTheStub_IsLeftAlone()
    {
        // Arrange / Act
        var stub = OutputStub.For("shell", "c1", OutputStub.Text("c1"));

        // Assert
        stub.Should().BeNull();
    }

    [Theory]
    [InlineData("[The output of this call was removed to save context. To read it again…]", true)]
    [InlineData("Error: exit 1\n[The output of this call was removed to save context. To read it again…]", true)]
    [InlineData("plain output", false)]
    [InlineData(null, false)]
    public void IsStub_Results_TellsStubsApart(string? result, bool expected)
    {
        // Act
        var isStub = OutputStub.IsStub(result);

        // Assert
        isStub.Should().Be(expected);
    }

    private static string Text(int tokens) => new('x', tokens * TokenEstimate.CharactersPerToken);
}
