using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Agent.Events;
using BlazeHarness.Core.Tools;
using BlazeHarness.Tests.Shared.Agent;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class ChatRunnerInjectionTests
{
    [Fact]
    public async Task RunAsync_MessageQueuedDuringToolRound_IsAddedAfterTheToolResult()
    {
        // Arrange
        var read = new FakeTool("read", isReadOnly: true);
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read")])
            .Enqueue([ScriptedChatClient.Text("fin")]);
        var history = NewHistory();
        var inbox = new FakeRunInbox("also this");

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken, tools: Tools(read), inbox: inbox);

        // Assert
        history.Select(m => m.Role).Should().Equal(ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.User, ChatRole.Assistant);
        history[3].Text.Should().Be("also this");
        events.Should().ContainInConsecutiveOrder(
            new ToolCallFinished("c1", "read", "read done", ToolCallStatus.Succeeded),
            new MessagesInjected("also this"),
            new NewResponse());
        client.Requests[1].Select(m => m.Text).Should().EndWith("also this");
    }

    [Fact]
    public async Task RunAsync_MessageQueuedDuringLastReply_ContinuesInsteadOfEnding()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("first")])
            .Enqueue([ScriptedChatClient.Text("second")]);
        var history = NewHistory();
        var inbox = new FakeRunInbox("next");

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken, inbox: inbox);

        // Assert
        history.Select(m => m.Text).Should().Equal("hi", "first", "next", "second");
        events.Should().ContainInConsecutiveOrder(
            new TextDelta("first"),
            new MessagesInjected("next"),
            new NewResponse(),
            new TextDelta("second"),
            new Done(StopReason.Completed));
        events.OfType<Done>().Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_NothingQueued_EndsAfterTheFirstReply()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("only")]);
        var history = NewHistory();
        var inbox = new FakeRunInbox();

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken, inbox: inbox);

        // Assert
        events.Should().NotContain(e => e is MessagesInjected);
        events[^1].Should().Be(new Done(StopReason.Completed));
        client.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task RunAsync_EmptyReplyThenQueuedMessage_AddsPlaceholderReplyBetweenUserMessages()
    {
        // Arrange
        using var client = new ScriptedChatClient([])
            .Enqueue([ScriptedChatClient.Text("answer")]);
        var history = NewHistory();
        var inbox = new FakeRunInbox("again");

        // Act
        await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken, inbox: inbox);

        // Assert
        history.Select(m => m.Role).Should().Equal(ChatRole.User, ChatRole.Assistant, ChatRole.User, ChatRole.Assistant);
        history[1].Text.Should().Be(Chat.EmptyReplyText);
    }

    [Fact]
    public async Task RunAsync_ReplyWroteCallAsText_DoesNotTakeMessagesBeforeTheRetry()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("<tool_call>x")])
            .Enqueue([ScriptedChatClient.Text("fine")])
            .Enqueue([ScriptedChatClient.Text("done")]);
        var history = NewHistory();
        var inbox = new FakeRunInbox("later");

        // Act
        await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken, tools: Tools(new FakeTool("read", isReadOnly: true)), inbox: inbox);

        // Assert
        history.Select(m => m.Text).Should().Equal("hi", "<tool_call>x", ChatRunner.TextCallNotice, "fine", "later", "done");
    }

    [Fact]
    public async Task RunAsync_CancelledDuringToolRound_LeavesTheMessagesInTheInbox()
    {
        // Arrange
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var read = new FakeTool("read", isReadOnly: true, async (_, ct) =>
        {
            await cts.CancelAsync();
            ct.ThrowIfCancellationRequested();
            return ToolResult.Ok("never");
        });
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read")]);
        var history = NewHistory();
        var inbox = new FakeRunInbox("kept");

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, history, cts.Token, tools: Tools(read), inbox: inbox);

        // Assert
        inbox.Calls.Should().Be(0);
        events.Should().NotContain(e => e is MessagesInjected);
        events[^1].Should().Be(new Done(StopReason.Cancelled));
    }

    private static List<ChatMessage> NewHistory() => [new(ChatRole.User, "hi")];

    private static ToolSet Tools(params ITool[] tools) => new(tools, new ToolContext(Path.GetTempPath()), TimeProvider.System);
}
