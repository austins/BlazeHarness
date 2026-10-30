using BlazeHarness.Core.Agent;
using BlazeHarness.Tests.Shared.Agent;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class ChatRunnerTests
{
    [Fact]
    public async Task RunAsync_TextReasoningAndUsage_EmitsEventsAndAppendsReply()
    {
        // Arrange
        using var client = new ScriptedChatClient(
            [
                Update(new TextReasoningContent("hmm")),
                Update(new TextContent("Hel")),
                Update(new TextContent("lo")),
                Update(new UsageContent(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 2, CachedInputTokenCount = 4 })),
            ]);
        var history = new List<ChatMessage> { new(ChatRole.User, "hi") };

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken);

        // Assert
        events.Should().Equal(
            new ReasoningDelta("hmm"),
            new TextDelta("Hel"),
            new TextDelta("lo"),
            new UsageReported(10, 2, 4),
            new Done(StopReason.Completed));
        history.Should().HaveCount(2);
        history[1].Role.Should().Be(ChatRole.Assistant);
        history[1].Text.Should().Be("Hello");
        history[1].Contents[0].Should().BeOfType<TextReasoningContent>().Which.Text.Should().Be("hmm");
    }

    [Fact]
    public async Task RunAsync_ThinkingWithoutAnAnswer_AddsNothingToHistory()
    {
        // Arrange
        using var client = new ScriptedChatClient([Update(new TextReasoningContent("hmm"))]);
        var history = new List<ChatMessage> { new(ChatRole.User, "hi") };

        // Act
        await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken);

        // Assert
        history.Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_ClientThrows_KeepsPartialReplyAndReportsError()
    {
        // Arrange
        using var client = new ScriptedChatClient([Update(new TextContent("par"))], new InvalidOperationException("boom"));
        var history = new List<ChatMessage> { new(ChatRole.User, "hi") };

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken);

        // Assert
        events.Should().Equal(new TextDelta("par"), new AgentError("boom"), new Done(StopReason.Error));
        history[1].Text.Should().Be("par");
    }

    [Fact]
    public async Task RunAsync_Cancelled_EndsWithCancelledAndKeepsPartialReply()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        using var client = new ScriptedChatClient([Update(new TextContent("so far"))], cancelAfterwards: cts);
        var history = new List<ChatMessage> { new(ChatRole.User, "hi") };

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, history, cts.Token);

        // Assert
        events.Should().Equal(new TextDelta("so far"), new Done(StopReason.Cancelled));
        history[1].Text.Should().Be("so far");
    }

    [Fact]
    public async Task RunAsync_EmptyReply_DoesNotAppendAssistantMessage()
    {
        // Arrange
        using var client = new ScriptedChatClient([]);
        var history = new List<ChatMessage> { new(ChatRole.User, "hi") };

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken);

        // Assert
        events.Should().Equal(new Done(StopReason.Completed));
        history.Should().ContainSingle();
    }

    private static ChatResponseUpdate Update(AIContent content) => new(ChatRole.Assistant, [content]);
}
