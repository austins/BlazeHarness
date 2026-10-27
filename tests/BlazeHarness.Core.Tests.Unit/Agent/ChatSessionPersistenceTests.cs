using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Sessions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class ChatSessionPersistenceTests
{
    private readonly RecordingSessionLog _log = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task SendAsync_SavesTheUserMessageThenTheReplyInBatches()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("Hel"), ScriptedChatClient.Text("lo")]);
        using var sut = Create(client);

        // Act
        await sut.SendAsync("hi", TestTargets.Create());

        // Assert
        _log.Events.Select(e => e.GetType()).Should().Equal(
            typeof(UserMessageEvent),
            typeof(AssistantStartEvent),
            typeof(AssistantDeltaEvent),
            typeof(AssistantEndEvent));
        _log.Events.OfType<AssistantDeltaEvent>().Single().Text.Should().Be("Hello");
        _log.Events.OfType<AssistantEndEvent>().Single().Status.Should().Be(EntryStatus.Done);
        _log.Events.OfType<AssistantStartEvent>().Single().EntryId.Should().Be(sut.Entries[1].Id);
    }

    [Fact]
    public async Task SendAsync_WhenTheFlushIntervalPasses_SavesTextBeforeTheReplyEnds()
    {
        // Arrange
        var time = new FakeTimeProvider { AutoAdvanceAmount = TimeSpan.FromMilliseconds(300) };
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("a"), ScriptedChatClient.Text("b"), ScriptedChatClient.Text("c")]);
        using var sut = Create(client, time);

        // Act
        await sut.SendAsync("hi", TestTargets.Create());

        // Assert
        var deltas = _log.Events.OfType<AssistantDeltaEvent>().ToList();
        deltas.Count.Should().BeGreaterThan(1);
        string.Concat(deltas.Select(d => d.Text)).Should().Be("abc");
    }

    [Fact]
    public async Task SendAsync_ReplyFails_SavesTheFailureWithTheTextSoFar()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("part")], failure: new InvalidOperationException("boom"));
        using var sut = Create(client);

        // Act
        await sut.SendAsync("hi", TestTargets.Create());

        // Assert
        var end = _log.Events.OfType<AssistantEndEvent>().Single();
        end.Status.Should().Be(EntryStatus.Failed);
        end.Error.Should().Be("boom");
        _log.Events.OfType<AssistantDeltaEvent>().Single().Text.Should().Be("part");
    }

    [Fact]
    public async Task SendAsync_SavingFails_StillCompletesTheReply()
    {
        // Arrange
        _log.FailWith = new IOException("disk full");
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        using var sut = Create(client);

        // Act
        await sut.SendAsync("hi", TestTargets.Create());

        // Assert
        sut.Entries[1].Text.Should().Be("ok");
        sut.Entries[1].Status.Should().Be(EntryStatus.Done);
    }

    [Fact]
    public async Task RetryAsync_SavesARewindToTheUserMessage()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("part")], failure: new InvalidOperationException("boom"))
            .Enqueue([ScriptedChatClient.Text("good")]);
        using var sut = Create(client);
        await sut.SendAsync("hi", TestTargets.Create());
        var userId = sut.Entries[0].Id;

        // Act
        await sut.RetryAsync(TestTargets.Create());

        // Assert
        _log.Events.OfType<RewindEvent>().Single().ToEntryId.Should().Be(userId);
        _log.Events.Select(e => e.GetType()).Should().ContainInConsecutiveOrder(typeof(RewindEvent), typeof(AssistantStartEvent));
    }

    [Fact]
    public async Task SendAsync_WhileAReplyRuns_IsIgnored()
    {
        // Arrange
        var gate = new TaskCompletionSource<IChatClient>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("first")]);
        #pragma warning disable IDISP013, VSTHRD003 // The test holds the factory open with its own task source.
        using var sut = new ChatSession((_, _) => gate.Task, _log, _time);
#pragma warning restore IDISP013, VSTHRD003
        var first = sut.SendAsync("one", TestTargets.Create());

        // Act
        await sut.SendAsync("two", TestTargets.Create());
        gate.SetResult(client);
        await first.Bounded();

        // Assert
        sut.Entries.Select(e => e.Text).Should().Equal("one", "first");
    }

    [Fact]
    public async Task Entries_WhileStreaming_IsASnapshotThatDoesNotChange()
    {
        // Arrange
        var gate = new TaskCompletionSource<IChatClient>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("x")]);
        #pragma warning disable IDISP013, VSTHRD003 // The test holds the factory open with its own task source.
        using var sut = new ChatSession((_, _) => gate.Task, _log, _time);
#pragma warning restore IDISP013, VSTHRD003
        var turn = sut.SendAsync("one", TestTargets.Create());
        var before = sut.Entries;

        // Act
        gate.SetResult(client);
        await turn.Bounded();

        // Assert
        before.Should().HaveCountLessThanOrEqualTo(2);
        sut.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task Restore_RebuildsEntriesAndHistoryFromASavedSession()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("Hel"), ScriptedChatClient.Text("lo")]);
        using var original = Create(client);
        await original.SendAsync("hi", TestTargets.Create());
        using var sut = new ChatSession((_, _) => throw new InvalidOperationException());

        // Act
        sut.Restore(await _log.ReadAsync(TestContext.Current.CancellationToken));

        // Assert
        sut.Entries.Select(e => (e.Id, e.Role, e.Text, e.Status)).Should().Equal(original.Entries.Select(e => (e.Id, e.Role, e.Text, e.Status)));
        sut.History.Select(m => m.Text).Should().Equal("hi", "Hello");
        sut.Entries[1].Label.Should().Be("P1 · fast");
    }

    [Fact]
    public async Task SendAsync_StampsTheUserMessageWhenSentAndTheReplyWhenFinished()
    {
        // Arrange
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 14, 55, 0, TimeSpan.Zero)) { AutoAdvanceAmount = TimeSpan.FromMinutes(1) };
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        using var sut = Create(client, time);

        // Act
        await sut.SendAsync("hi", TestTargets.Create());

        // Assert
        sut.Entries[0].At.Should().Be(_log.Events.OfType<UserMessageEvent>().Single().At);
        sut.Entries[1].At.Should().Be(_log.Events.OfType<AssistantEndEvent>().Single().At);
        sut.Entries[1].At.Should().BeAfter(sut.Entries[0].At);
    }

    [Fact]
    public void Restore_KeepsTheOriginalTimes_AndAnInterruptedReplyKeepsItsStartTime()
    {
        // Arrange
        var first = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
        var done = Guid.NewGuid();
        var cut = Guid.NewGuid();
        using var sut = new ChatSession((_, _) => throw new InvalidOperationException());

        // Act
        sut.Restore(
        [
            new UserMessageEvent(first, Guid.NewGuid(), "one"),
            new AssistantStartEvent(first.AddMinutes(1), done, null),
            new AssistantEndEvent(first.AddMinutes(2), done, EntryStatus.Done, null, null),
            new UserMessageEvent(first.AddMinutes(3), Guid.NewGuid(), "two"),
            new AssistantStartEvent(first.AddMinutes(4), cut, null),
            new AssistantDeltaEvent(first.AddMinutes(5), cut, "half", string.Empty),
        ]);

        // Assert
        sut.Entries.Select(e => e.At).Should().Equal(first, first.AddMinutes(2), first.AddMinutes(3), first.AddMinutes(4));
    }

    [Fact]
    public void Restore_ReplyWithNoEnd_IsInterruptedAndCanBeRetried()
    {
        // Arrange
        var user = Guid.NewGuid();
        var reply = Guid.NewGuid();
        var at = DateTimeOffset.UnixEpoch;
        using var sut = new ChatSession((_, _) => throw new InvalidOperationException());

        // Act
        sut.Restore(
        [
            new UserMessageEvent(at, user, "hi"),
            new AssistantStartEvent(at, reply, "P1 · fast"),
            new AssistantDeltaEvent(at, reply, "half an ans", string.Empty),
        ]);

        // Assert
        sut.Entries[1].Status.Should().Be(EntryStatus.Interrupted);
        sut.Entries[1].Text.Should().Be("half an ans");
        sut.CanRetry.Should().BeTrue();
        sut.History.Select(m => m.Text).Should().Equal("hi", "half an ans");
    }

    [Fact]
    public void Restore_ReplyCutOffBeforeAnyText_KeepsAReplyBetweenTheUserMessages()
    {
        // Arrange
        var at = DateTimeOffset.UnixEpoch;
        var reply = Guid.NewGuid();
        using var sut = new ChatSession((_, _) => throw new InvalidOperationException());

        // Act
        sut.Restore(
        [
            new UserMessageEvent(at, Guid.NewGuid(), "a"),
            new AssistantStartEvent(at, reply, "P1 · fast"),
            new UserMessageEvent(at, Guid.NewGuid(), "b"),
        ]);

        // Assert
        sut.History.Select(m => m.Role).Should().Equal(ChatRole.User, ChatRole.Assistant, ChatRole.User);
    }

    [Fact]
    public async Task EditAsync_SavesARewindThenTheNewMessage()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("one")])
            .Enqueue([ScriptedChatClient.Text("two")])
            .Enqueue([ScriptedChatClient.Text("again")]);
        using var sut = Create(client);
        await sut.SendAsync("a", TestTargets.Create());
        await sut.SendAsync("b", TestTargets.Create());
        var keep = sut.Entries[1].Id;

        // Act
        await sut.EditAsync(sut.Entries[2].Id, "b2", TestTargets.Create());

        // Assert
        _log.Events.OfType<RewindEvent>().Single().ToEntryId.Should().Be(keep);
        _log.Events.Select(e => e.GetType()).TakeLast(5).Should().Equal(
            typeof(RewindEvent),
            typeof(UserMessageEvent),
            typeof(AssistantStartEvent),
            typeof(AssistantDeltaEvent),
            typeof(AssistantEndEvent));
        _log.Events.OfType<UserMessageEvent>().Last().Text.Should().Be("b2");
    }

    [Fact]
    public async Task Restore_AfterEditingTheFirstMessage_ShowsOnlyTheNewConversation()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("one")])
            .Enqueue([ScriptedChatClient.Text("fresh")]);
        using var edited = Create(client);
        await edited.SendAsync("a", TestTargets.Create());
        await edited.EditAsync(edited.Entries[0].Id, "a2", TestTargets.Create());
        using var sut = new ChatSession((_, _) => throw new InvalidOperationException());

        // Act
        sut.Restore(await _log.ReadAsync(TestContext.Current.CancellationToken));

        // Assert
        sut.Entries.Select(e => e.Text).Should().Equal("a2", "fresh");
        sut.History.Select(m => m.Text).Should().Equal("a2", "fresh");
    }

    [Fact]
    public void Restore_Rewind_DropsLaterMessagesFromTheConversation()
    {
        // Arrange
        var user = Guid.NewGuid();
        var failed = Guid.NewGuid();
        var good = Guid.NewGuid();
        var at = DateTimeOffset.UnixEpoch;
        using var sut = new ChatSession((_, _) => throw new InvalidOperationException());

        // Act
        sut.Restore(
        [
            new UserMessageEvent(at, user, "hi"),
            new AssistantStartEvent(at, failed, null),
            new AssistantDeltaEvent(at, failed, "bad", string.Empty),
            new AssistantEndEvent(at, failed, EntryStatus.Failed, "boom", null),
            new RewindEvent(at, user),
            new AssistantStartEvent(at, good, null),
            new AssistantDeltaEvent(at, good, "good", string.Empty),
            new AssistantEndEvent(at, good, EntryStatus.Done, null, null),
        ]);

        // Assert
        sut.Entries.Select(e => e.Text).Should().Equal("hi", "good");
        sut.History.Select(m => m.Text).Should().Equal("hi", "good");
    }

    [Fact]
    public async Task Restore_ThenSend_ContinuesWithTheSavedHistory()
    {
        // Arrange
        var at = DateTimeOffset.UnixEpoch;
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("again")]);
        using var sut = Create(client);
        var user = Guid.NewGuid();
        var reply = Guid.NewGuid();
        sut.Restore(
        [
            new UserMessageEvent(at, user, "hi"),
            new AssistantStartEvent(at, reply, null),
            new AssistantDeltaEvent(at, reply, "hello", string.Empty),
            new AssistantEndEvent(at, reply, EntryStatus.Done, null, null),
        ]);

        // Act
        await sut.SendAsync("more", TestTargets.Create());

        // Assert
        client.Requests[0].Select(m => m.Text).Should().Equal("hi", "hello", "more");
    }

    [Fact]
    public async Task Restore_NotEmpty_Throws()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("x")]);
        using var sut = Create(client);
        await sut.SendAsync("hi", TestTargets.Create());

        // Act
        var act = () => sut.Restore([]);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    private ChatSession Create(ScriptedChatClient client, TimeProvider? time = null) =>
        new((_, _) => Task.FromResult<IChatClient>(client), _log, time ?? _time);
}
