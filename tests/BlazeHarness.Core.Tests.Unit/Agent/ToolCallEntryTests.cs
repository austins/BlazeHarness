using BlazeHarness.Core.Agent;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class ToolCallEntryTests
{
    [Theory]
    [InlineData(ToolCallStatus.Pending, false)]
    [InlineData(ToolCallStatus.AwaitingApproval, false)]
    [InlineData(ToolCallStatus.Running, false)]
    [InlineData(ToolCallStatus.Succeeded, true)]
    [InlineData(ToolCallStatus.Failed, true)]
    [InlineData(ToolCallStatus.Denied, true)]
    [InlineData(ToolCallStatus.Cancelled, true)]
    [InlineData(ToolCallStatus.Interrupted, true)]
    public void IsFinished_Status_MatchesWhetherTheCallHasEnded(ToolCallStatus status, bool expected)
    {
        // Arrange
        var sut = new ToolCallEntry("c1", "read", "{}", string.Empty) { Status = status };

        // Act
        var finished = sut.IsFinished;

        // Assert
        finished.Should().Be(expected);
    }

    [Fact]
    public void ForModel_FailedCall_PrefixesTheError()
    {
        // Arrange & Act
        var text = ToolCallEntry.ForModel(ToolCallStatus.Failed, "no such file");

        // Assert
        text.Should().Be("Error: no such file");
    }

    [Fact]
    public void ForModel_SucceededCall_ReturnsTheOutputAsIs()
    {
        // Arrange & Act
        var text = ToolCallEntry.ForModel(ToolCallStatus.Succeeded, "contents");

        // Assert
        text.Should().Be("contents");
    }

    [Fact]
    public void Unfinished_InterruptedAndCancelled_SayWhoStoppedTheCall()
    {
        // Arrange & Act
        var interrupted = ToolCallEntry.Unfinished(ToolCallStatus.Interrupted);
        var cancelled = ToolCallEntry.Unfinished(ToolCallStatus.Cancelled);

        // Assert
        interrupted.Should().Contain("app");
        cancelled.Should().Contain("user");
    }
}
