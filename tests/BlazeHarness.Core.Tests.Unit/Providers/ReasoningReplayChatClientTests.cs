using BlazeHarness.Core.Providers;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Unit.Providers;

public sealed class ReasoningReplayChatClientTests
{
    [Fact]
    public void WithReasoning_UserMessageInBetween_KeepsTheEarlierThinking()
    {
        // Arrange
        List<ChatMessage> messages = [new(ChatRole.User, "hi"), Step("c1"), Result("c1"), new(ChatRole.User, "more"), Step("c2"), Result("c2")];

        // Act
        var result = ReasoningReplayChatClient.WithReasoning(messages);

        // Assert
        result[1].RawRepresentation.Should().NotBeNull();
        result[4].RawRepresentation.Should().NotBeNull();
    }

    [Fact]
    public void WithReasoning_NoThinking_LeavesTheMessage()
    {
        // Arrange
        List<ChatMessage> messages = [new(ChatRole.User, "hi"), new(ChatRole.Assistant, "answer")];

        // Act
        var result = ReasoningReplayChatClient.WithReasoning(messages);

        // Assert
        result[1].Should().BeSameAs(messages[1]);
    }

    private static ChatMessage Step(string callId) =>
        new(ChatRole.Assistant, [new TextReasoningContent("thinking"), new FunctionCallContent(callId, "read")]);

    private static ChatMessage Result(string callId) => new(ChatRole.Tool, [new FunctionResultContent(callId, "ok")]);
}
