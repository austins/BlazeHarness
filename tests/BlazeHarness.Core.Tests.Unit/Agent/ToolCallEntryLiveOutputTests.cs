using BlazeHarness.Core.Agent;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class ToolCallEntryLiveOutputTests
{
    private readonly ToolCallEntry _sut = new("c1", "shell", "{}", "ls");

    [Fact]
    public void LiveOutput_LinesAppended_ShowsThem()
    {
        // Act
        _sut.AppendLiveLine("one");
        _sut.AppendLiveLine("two");

        // Assert
        _sut.LiveOutput.Should().Be("one\ntwo");
    }

    [Fact]
    public void EndLiveOutput_AfterLines_ClearsThem()
    {
        // Arrange
        _sut.AppendLiveLine("one");

        // Act
        _sut.EndLiveOutput();

        // Assert
        _sut.LiveOutput.Should().BeEmpty();
    }
}
