using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Chats;
using BlazeHarness.Core.Chats.Events;
using BlazeHarness.Core.Tools;
using BlazeHarness.Tests.Shared;
using BlazeHarness.Tests.Shared.Agent;
using BlazeHarness.Tests.Shared.Chats;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class ChatQueueTests
{
    private readonly RecordingChatLog _log = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Queue_NoReplyRunning_ReturnsFalseAndKeepsNothing()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        using var sut = Create(client);
        await sut.SendAsync("hi", TestTargets.Create());

        // Act
        var accepted = sut.Queue("late");

        // Assert
        accepted.Should().BeFalse();
        sut.Queued.Should().BeEmpty();
    }

    [Fact]
    public async Task Queue_BlankText_ReturnsFalse()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read")]).Enqueue([ScriptedChatClient.Text("fin")]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sut = Create(client, HoldingTools(started, release));
        var turn = sut.SendAsync("hi", TestTargets.Create());
        await started.Task.Bounded();

        // Act
        var accepted = sut.Queue("   ");
        release.SetResult();
        await turn.Bounded();

        // Assert
        accepted.Should().BeFalse();
        sut.Queued.Should().BeEmpty();
    }

    [Fact]
    public async Task Queue_WhileAToolRuns_AddsOneMessageAfterTheToolResult()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read")]).Enqueue([ScriptedChatClient.Text("fin")]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sut = Create(client, HoldingTools(started, release));
        var turn = sut.SendAsync("hi", TestTargets.Create());
        await started.Task.Bounded();

        // Act
        var first = sut.Queue("one");
        var second = sut.Queue("two");
        release.SetResult();
        await turn.Bounded();

        // Assert
        first.Should().BeTrue();
        second.Should().BeTrue();
        sut.Queued.Should().BeEmpty();
        sut.Entries.Select(e => e.Role).Should().Equal(ChatRole.User, ChatRole.Assistant, ChatRole.User, ChatRole.Assistant);
        sut.Entries[2].Text.Should().Be("one\n\ntwo");
        sut.Entries[2].Origin.Should().Be(UserMessageOrigin.Steer);
        sut.Entries[0].Origin.Should().BeNull();
        client.Requests[1].Select(m => m.Role).Should().Equal(ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.User);
        client.Requests[1][^1].Text.Should().Be("one\n\ntwo");
        _log.Events.OfType<UserMessageEvent>().Select(e => e.Origin).Should().Equal(null, UserMessageOrigin.Steer);
    }

    [Fact]
    public async Task Queue_AfterReplyWhileAToolRuns_AddsTheMessageOnlyAfterTheFinalReply()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read")])
            .Enqueue([ScriptedChatClient.Text("fin")])
            .Enqueue([ScriptedChatClient.Text("next")]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sut = Create(client, HoldingTools(started, release));
        var turn = sut.SendAsync("hi", TestTargets.Create());
        await started.Task.Bounded();

        // Act
        var accepted = sut.Queue("later", QueueMode.AfterReply);
        release.SetResult();
        await turn.Bounded();

        // Assert
        accepted.Should().BeTrue();
        sut.Queued.Should().BeEmpty();
        sut.Entries.Select(e => e.Text).Should().Equal("hi", string.Empty, "fin", "later", "next");
        client.Requests[1].Select(m => m.Role).Should().Equal(ChatRole.User, ChatRole.Assistant, ChatRole.Tool);
    }

    [Fact]
    public async Task Queue_SteerAndAfterReplyWhileAToolRuns_AddsOnlyTheSteerMessageAfterTheToolResult()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read")])
            .Enqueue([ScriptedChatClient.Text("fin")])
            .Enqueue([ScriptedChatClient.Text("next")]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sut = Create(client, HoldingTools(started, release));
        var turn = sut.SendAsync("hi", TestTargets.Create());
        await started.Task.Bounded();

        // Act
        sut.Queue("later", QueueMode.AfterReply);
        sut.Queue("now");
        release.SetResult();
        await turn.Bounded();

        // Assert
        sut.Entries.Select(e => e.Text).Should().Equal("hi", string.Empty, "now", "fin", "later", "next");
        client.Requests[1][^1].Text.Should().Be("now");
    }

    [Fact]
    public async Task Queue_WhileAToolRuns_RestoredChatEqualsTheLiveChat()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read")]).Enqueue([ScriptedChatClient.Text("fin")]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var live = Create(client, HoldingTools(started, release));
        var turn = live.SendAsync("hi", TestTargets.Create());
        await started.Task.Bounded();
        live.Queue("more");
        release.SetResult();
        await turn.Bounded();

        // Act
        using var sut = Create(client);
        sut.Restore(_log.Events);

        // Assert
        sut.Entries.Select(e => (e.Role, e.Text, e.Origin)).Should().Equal(live.Entries.Select(e => (e.Role, e.Text, e.Origin)));
        sut.History.Select(m => (m.Role, m.Text)).Should().Equal(live.History.Select(m => (m.Role, m.Text)));
        sut.Entries.Select(e => e.HistoryIndex).Should().Equal(live.Entries.Select(e => e.HistoryIndex));
    }

    [Fact]
    public async Task Queue_WhileTheLastReplyStreams_ContinuesWithAnotherReply()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("first")]).Enqueue([ScriptedChatClient.Text("second")]);
        Chat? holder = null;
        var queued = false;
        var log = Substitute.For<IChatLog>();
        log.AppendAsync(Arg.Any<IReadOnlyCollection<ChatEvent>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (!queued && call.Arg<IReadOnlyCollection<ChatEvent>>().OfType<AssistantEndEvent>().Any())
                {
                    queued = true;
                    holder!.Queue("after");
                }

                return Task.CompletedTask;
            });
        using var sut = Create(client, log: log);
#pragma warning disable IDISP001 // The chat is disposed by the using declaration; the log callback only needs a reference.
        holder = sut;
#pragma warning restore IDISP001

        // Act
        await sut.SendAsync("hi", TestTargets.Create());

        // Assert
        sut.Entries.Select(e => e.Text).Should().Equal("hi", "first", "after", "second");
        sut.Entries[2].Origin.Should().Be(UserMessageOrigin.Steer);
        sut.IsRunning.Should().BeFalse();
        sut.Queued.Should().BeEmpty();
    }

    [Fact]
    public async Task Stop_WithQueuedMessage_KeepsItQueuedAndSendsNoMoreRequests()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read")]).Enqueue([ScriptedChatClient.Text("never")]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sut = Create(client, HoldingTools(started, release));
        var turn = sut.SendAsync("hi", TestTargets.Create());
        await started.Task.Bounded();
        sut.Queue("keep me");

        // Act
        sut.Stop();
        await turn.Bounded();

        // Assert
        client.Requests.Should().HaveCount(1);
        sut.Queued.Should().Equal(new QueuedMessage("keep me", QueueMode.Steer));
        sut.Entries.Should().NotContain(e => e.Origin == UserMessageOrigin.Steer);
        sut.TakeQueued().Should().Equal("keep me");
        sut.Queued.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveQueued_WhileAToolRuns_DropsOnlyThatMessage()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read")]).Enqueue([ScriptedChatClient.Text("fin")]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sut = Create(client, HoldingTools(started, release));
        var turn = sut.SendAsync("hi", TestTargets.Create());
        await started.Task.Bounded();
        sut.Queue("one");
        sut.Queue("two");

        // Act
        var removed = sut.RemoveQueued(0, "one");
        release.SetResult();
        await turn.Bounded();

        // Assert
        removed.Should().BeTrue();
        sut.Entries[2].Text.Should().Be("two");
        client.Requests[1][^1].Text.Should().Be("two");
    }

    [Fact]
    public async Task RemoveQueued_TextNotAtThatPosition_ReturnsFalseAndKeepsTheMessage()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read")]).Enqueue([ScriptedChatClient.Text("fin")]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sut = Create(client, HoldingTools(started, release));
        var turn = sut.SendAsync("hi", TestTargets.Create());
        await started.Task.Bounded();
        sut.Queue("one");

        // Act
        var otherText = sut.RemoveQueued(0, "other");
        var pastTheEnd = sut.RemoveQueued(1, "one");
        var queued = sut.Queued;
        release.SetResult();
        await turn.Bounded();

        // Assert
        otherText.Should().BeFalse();
        pastTheEnd.Should().BeFalse();
        queued.Select(m => m.Text).Should().Equal("one");
    }

    [Fact]
    public async Task SendAsync_AfterAnAddedMessageTheRunIsStoppedDuringCompaction_RestoredChatEqualsTheLiveChat()
    {
        // Arrange
        var usage = new UsageContent(new UsageDetails { InputTokenCount = 6_000, OutputTokenCount = 10 });
        using var client = new ScriptedChatClient([new ChatResponseUpdate(ChatRole.Assistant, [usage])])
            .Enqueue([ScriptedChatClient.Text("sum")]);
        Chat? holder = null;
        var log = Substitute.For<IChatLog>();
        log.AppendAsync(Arg.Any<IReadOnlyCollection<ChatEvent>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var events = call.Arg<IReadOnlyCollection<ChatEvent>>();
                await _log.AppendAsync(events);
                if (events.OfType<AssistantStartEvent>().Any())
                {
                    holder!.Queue("steer");
                }

                if (events.OfType<UserMessageEvent>().Any(e => e.Origin == UserMessageOrigin.Steer))
                {
                    holder!.Stop();
                }
            });
        using var live = Create(client, log: log);
#pragma warning disable IDISP001 // The chat is disposed by the using declaration; the log callback only needs a reference.
        holder = live;
#pragma warning restore IDISP001
        await live.SendAsync(new string('x', 8_000), TestTargets.Create());

        // Act
        using var sut = Create(client);
        sut.Restore(_log.Events);

        // Assert
        client.Requests.Should().HaveCount(2);
        live.Entries.Select(e => (e.Role, e.Status)).Should().Equal(
            (ChatRole.User, EntryStatus.Done),
            (ChatRole.Assistant, EntryStatus.Done),
            (ChatRole.User, EntryStatus.Done),
            (ChatRole.Assistant, EntryStatus.Cancelled));
        _log.Events.OfType<AssistantEndEvent>().Select(e => e.EntryId).Should().OnlyHaveUniqueItems();
        sut.Entries.Select(e => (e.Role, e.Text, e.Status)).Should().Equal(live.Entries.Select(e => (e.Role, e.Text, e.Status)));
        sut.History.Select(m => (m.Role, m.Text)).Should().Equal(live.History.Select(m => (m.Role, m.Text)));
        live.CanRetry.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_AMessageQueuedForAnEarlierReplyIsStillThere_DoesNotInjectItIntoTheNewReply()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read")]).Enqueue([ScriptedChatClient.Text("never")]).Enqueue([ScriptedChatClient.Text("ok")]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sut = Create(client, HoldingTools(started, release));
        var turn = sut.SendAsync("hi", TestTargets.Create());
        await started.Task.Bounded();
        sut.Queue("stale");
        sut.Stop();
        await turn.Bounded();

        // Act
        await sut.SendAsync("fresh", TestTargets.Create());

        // Assert
        client.Requests[^1].Select(m => m.Text).Should().NotContain("stale");
        sut.Entries.Should().NotContain(e => e.Origin == UserMessageOrigin.Steer);
        sut.Queued.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_AfterTheQueuedMessagesWereTaken_DoesNotSendThemAgain()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read")]).Enqueue([ScriptedChatClient.Text("ok")]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sut = Create(client, HoldingTools(started, release));
        var turn = sut.SendAsync("hi", TestTargets.Create());
        await started.Task.Bounded();
        sut.Queue("old");
        sut.Stop();
        await turn.Bounded();
        sut.TakeQueued();

        // Act
        await sut.SendAsync("fresh", TestTargets.Create());

        // Assert
        client.Requests[^1].Select(m => m.Text).Should().NotContain("old");
    }

    [Fact]
    public async Task CanRetry_AfterAStoppedTurnWithAnAddedMessage_IsTrueAndRetryKeepsIt()
    {
        // Arrange
        var at = DateTimeOffset.UnixEpoch;
        var user = Guid.NewGuid();
        var reply = Guid.NewGuid();
        var steer = Guid.NewGuid();
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("again")]);
        using var sut = Create(client);
        sut.Restore(
        [
            new UserMessageEvent(at, user, "hi"),
            new AssistantStartEvent(at, reply, null),
            new AssistantDeltaEvent(at, reply, "part", string.Empty),
            new AssistantEndEvent(at, reply, EntryStatus.Done, null, null),
            new UserMessageEvent(at, steer, "more", null, UserMessageOrigin.Steer),
        ]);

        // Act
        var canRetry = sut.CanRetry;
        await sut.RetryAsync(TestTargets.Create());

        // Assert
        canRetry.Should().BeTrue();
        client.Requests[0].Select(m => m.Text).Should().Equal("hi", "part", "more");
        sut.Entries.Select(e => e.Text).Should().Equal("hi", "part", "more", "again");
    }

    private Chat Create(ScriptedChatClient client, ToolSet? tools = null, IChatLog? log = null)
    {
        return new Chat((_, _) => Task.FromResult<IChatClient>(client), _time, log ?? _log) { Tools = tools };
    }

    private static ToolSet HoldingTools(TaskCompletionSource started, TaskCompletionSource release)
    {
        var read = new FakeTool("read", isReadOnly: true, async (_, ct) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(TestTimeouts.Default, ct);
            return ToolResult.Ok("read done");
        });
        return new ToolSet([read], new ToolContext(Path.GetTempPath()), TimeProvider.System);
    }
}
