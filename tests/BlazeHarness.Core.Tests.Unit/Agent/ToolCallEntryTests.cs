using BlazeHarness.Core.Agent;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class ToolCallEntryTests
{
    [Theory]
    [InlineData(ToolCallStatus.Pending, false)]
    [InlineData(ToolCallStatus.AwaitingApproval, false)]
    [InlineData(ToolCallStatus.AwaitingAnswer, false)]
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
        var interrupted = ToolCallEntry.Unfinished(ToolCallStatus.Interrupted, "read");
        var cancelled = ToolCallEntry.Unfinished(ToolCallStatus.Cancelled, "read");

        // Assert
        interrupted.Should().Contain("app");
        cancelled.Should().Contain("user");
    }

    [Fact]
    public void Question_AskUserCall_IsReadFromTheArguments()
    {
        // Arrange
        var sut = new ToolCallEntry("c1", "ask_user", """{"question":"Which?","options":["A","B"]}""", string.Empty);

        // Act
        var question = sut.Question;

        // Assert
        question!.Question.Should().Be("Which?");
        question.Options.Select(o => o.Label).Should().Equal("A", "B");
    }

    [Theory]
    [InlineData("ask_user", """{"question":"Which?","options":["A"]}""")]
    [InlineData("read", """{"question":"Which?"}""")]
    public void Question_InvalidOrOtherCall_IsNull(string name, string arguments)
    {
        // Arrange
        var sut = new ToolCallEntry("c1", name, arguments, string.Empty);

        // Act
        var question = sut.Question;

        // Assert
        question.Should().BeNull();
    }

    [Theory]
    [InlineData(ToolCallStatus.Cancelled, "The run was stopped before the user answered.")]
    [InlineData(ToolCallStatus.Interrupted, "The app stopped before the user answered. Treat the user's next message as the reply; ask again only if it still matters.")]
    public void Unfinished_AskUserCall_SaysWhyThereIsNoAnswer(ToolCallStatus status, string expected)
    {
        // Act
        var text = ToolCallEntry.Unfinished(status, "ask_user");

        // Assert
        text.Should().Be(expected);
    }
}
