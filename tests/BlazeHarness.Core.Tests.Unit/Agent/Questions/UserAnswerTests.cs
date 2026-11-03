using BlazeHarness.Core.Agent.Questions;

namespace BlazeHarness.Core.Tests.Unit.Agent.Questions;

public sealed class UserAnswerTests
{
    [Fact]
    public void ForModel_Chose_QuotesTheLabelsAndAppendsTheNote()
    {
        // Act
        var text = UserAnswer.Chose(["A", "B"], " because ").ForModel();

        // Assert
        text.Should().Be("The user chose: \"A\", \"B\". They added: because");
    }

    [Fact]
    public void ForModel_EachKind_UsesTheSharedWording()
    {
        // Assert
        UserAnswer.Chose(["A"]).ForModel().Should().StartWith(AnswerText.ChosePrefix);
        UserAnswer.Answered("hello").ForModel().Should().Be(AnswerText.AnsweredPrefix + "hello");
        UserAnswer.Skip().ForModel().Should().Be(AnswerText.Skipped);
        UserAnswer.LeaveToModel().ForModel().Should().Be(AnswerText.YouDecide);
    }

    [Fact]
    public void TryRead_ForModelOutput_RoundTrips()
    {
        // Arrange
        UserAnswer[] answers = [UserAnswer.Chose(["A"]), UserAnswer.Chose(["A", "B"], "note"), UserAnswer.Answered("free text"), UserAnswer.Skip(), UserAnswer.LeaveToModel()];

        // Act
        var read = answers.Select(a => UserAnswer.TryRead(a.ForModel())).ToList();

        // Assert
        read.Should().BeEquivalentTo(answers, options => options.WithStrictOrdering());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Error: nope")]
    [InlineData(AnswerText.Stopped)]
    [InlineData(AnswerText.Interrupted)]
    public void TryRead_NotAnAnswer_ReturnsNull(string output)
    {
        // Act
        var answer = UserAnswer.TryRead(output);

        // Assert
        answer.Should().BeNull();
    }

    [Fact]
    public void Display_EachKind_IsShortAndReadable()
    {
        // Assert
        UserAnswer.Chose(["A", "B"], "why").Display.Should().Be("A, B — why");
        UserAnswer.Chose(["A"]).Display.Should().Be("A");
        UserAnswer.Answered("text").Display.Should().Be("text");
        UserAnswer.Skip().Display.Should().Be("Skipped");
        UserAnswer.LeaveToModel().Display.Should().Be("You decide");
    }
}
