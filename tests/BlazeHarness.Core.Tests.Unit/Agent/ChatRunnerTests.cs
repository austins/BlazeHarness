using BlazeHarness.Core.Agent;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

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
        var events = await Collect(client, history, CancellationToken.None);

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
    }

    [Fact]
    public async Task RunAsync_ClientThrows_KeepsPartialReplyAndReportsError()
    {
        // Arrange
        using var client = new ScriptedChatClient([Update(new TextContent("par"))], new InvalidOperationException("boom"));
        var history = new List<ChatMessage> { new(ChatRole.User, "hi") };

        // Act
        var events = await Collect(client, history, CancellationToken.None);

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
        var events = await Collect(client, history, cts.Token);

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
        var events = await Collect(client, history, CancellationToken.None);

        // Assert
        events.Should().Equal(new Done(StopReason.Completed));
        history.Should().ContainSingle();
    }

    private static ChatResponseUpdate Update(AIContent content) => new(ChatRole.Assistant, [content]);

    private static async Task<List<AgentEvent>> Collect(IChatClient client, IList<ChatMessage> history, CancellationToken cancellationToken)
    {
        var events = new List<AgentEvent>();
        await foreach (var e in ChatRunner.RunAsync(client, history, new ChatOptions(), cancellationToken))
        {
            events.Add(e);
        }

        return events;
    }

    private sealed class ScriptedChatClient(
        IReadOnlyList<ChatResponseUpdate> updates,
        Exception? failure = null,
        CancellationTokenSource? cancelAfterwards = null) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var update in updates)
            {
                yield return update;
            }

            if (cancelAfterwards is not null)
            {
                await cancelAfterwards.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (failure is not null)
            {
                throw failure;
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
