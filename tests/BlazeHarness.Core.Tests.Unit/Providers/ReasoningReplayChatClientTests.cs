using BlazeHarness.Core.Attachments;
using BlazeHarness.Core.Providers;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Unit.Providers;

public sealed class ReasoningReplayChatClientTests
{
    [Fact]
    public void WithReasoning_PicturesOfToolCalls_KeepTheEarlierThinking()
    {
        // Arrange
        var pictures = AttachmentMessage.ForToolImages([("browser_screenshot", "c1", new DataContent(new byte[] { 1 }, "image/png"))])!;
        List<ChatMessage> messages = [new(ChatRole.User, "hi"), Step("c1"), Result("c1"), pictures, Step("c2"), Result("c2")];

        // Act
        var result = ReasoningReplayChatClient.WithReasoning(messages);

        // Assert
        result[1].RawRepresentation.Should().NotBeNull();
        result[4].RawRepresentation.Should().NotBeNull();
    }

    [Fact]
    public void WithReasoning_UserMessageInBetween_DropsTheEarlierThinking()
    {
        // Arrange
        List<ChatMessage> messages = [new(ChatRole.User, "hi"), Step("c1"), Result("c1"), new(ChatRole.User, "more"), Step("c2"), Result("c2")];

        // Act
        var result = ReasoningReplayChatClient.WithReasoning(messages);

        // Assert
        result[1].RawRepresentation.Should().BeNull();
        result[4].RawRepresentation.Should().NotBeNull();
    }

    private static ChatMessage Step(string callId) =>
        new(ChatRole.Assistant, [new TextReasoningContent("thinking"), new FunctionCallContent(callId, "read")]);

    private static ChatMessage Result(string callId) => new(ChatRole.Tool, [new FunctionResultContent(callId, "ok")]);
}
