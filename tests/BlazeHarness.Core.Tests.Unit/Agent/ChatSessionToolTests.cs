using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Sessions;
using BlazeHarness.Core.Tests.Unit.Sessions;
using BlazeHarness.Core.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class ChatSessionToolTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly RecordingSessionLog _log = new();
    private readonly FakeTimeProvider _time = new(Start);

    [Fact]
    public async Task SendAsync_ReadOnlyCall_AddsAnEntryPerResponseAndSavesTheCall()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("Looking"), ScriptedChatClient.Call("c1", "read", """{"a":1}""")])
            .Enqueue([ScriptedChatClient.Text("Found it")]);
        using var sut = Create(client, new FakeTool("read", isReadOnly: true));

        // Act
        await sut.SendAsync("go", TestTargets.Create());

        // Assert
        sut.Entries.Select(e => (e.Role.Value, e.Text)).Should().Equal(("user", "go"), ("assistant", "Looking"), ("assistant", "Found it"));
        var call = sut.Entries[1].ToolCalls.Should().ContainSingle().Subject;
        (call.Name, call.Status, call.Output, call.Summary).Should().Be(("read", ToolCallStatus.Succeeded, "read done", "fake read"));
        sut.Entries[1].Status.Should().Be(EntryStatus.Done);
        _log.Events.OfType<ToolCallEvent>().Single().ArgumentsJson.Should().Be("""{"a":1}""");
        _log.Events.OfType<ToolResultEvent>().Single().Status.Should().Be(ToolCallStatus.Succeeded);
        sut.History.Select(m => m.Role).Should().Equal(ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant);
    }

    [Fact]
    public async Task SendAsync_WriteCall_WaitsForApprovalThenRuns()
    {
        // Arrange
        var write = new FakeTool("write", isReadOnly: false);
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "write")])
            .Enqueue([ScriptedChatClient.Text("done")]);
        using var sut = Create(client, write);
        var turn = sut.SendAsync("go", TestTargets.Create());
        await WaitForStatus(sut, ToolCallStatus.AwaitingApproval);
        write.Started.Should().Be(0);

        // Act
        var answered = sut.Approve("c1", ApprovalChoice.AllowOnce);
        await turn;

        // Assert
        answered.Should().BeTrue();
        write.Started.Should().Be(1);
        sut.Entries[1].ToolCalls.Single().Status.Should().Be(ToolCallStatus.Succeeded);
    }

    [Fact]
    public async Task SendAsync_WriteCallDenied_RecordsTheDenial()
    {
        // Arrange
        var write = new FakeTool("write", isReadOnly: false);
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "write")])
            .Enqueue([ScriptedChatClient.Text("ok")]);
        using var sut = Create(client, write);
        var turn = sut.SendAsync("go", TestTargets.Create());
        await WaitForStatus(sut, ToolCallStatus.AwaitingApproval);

        // Act
        sut.Approve("c1", ApprovalChoice.Deny);
        await turn;

        // Assert
        write.Started.Should().Be(0);
        sut.Entries[1].ToolCalls.Single().Status.Should().Be(ToolCallStatus.Denied);
        _log.Events.OfType<ToolResultEvent>().Single().Status.Should().Be(ToolCallStatus.Denied);
    }

    [Fact]
    public async Task Stop_WhileAwaitingApproval_CancelsTheCallAndTheReply()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "write")]);
        using var sut = Create(client, new FakeTool("write", isReadOnly: false));
        var turn = sut.SendAsync("go", TestTargets.Create());
        await WaitForStatus(sut, ToolCallStatus.AwaitingApproval);

        // Act
        sut.Stop();
        await turn;

        // Assert
        sut.Entries[1].ToolCalls.Single().Status.Should().Be(ToolCallStatus.Cancelled);
        sut.Entries[1].Status.Should().Be(EntryStatus.Cancelled);
        sut.History[^1].Role.Should().Be(ChatRole.Tool);
        sut.Approve("c1", ApprovalChoice.AllowOnce).Should().BeFalse();
    }

    [Fact]
    public void Approve_NoCallWaiting_ReturnsFalse()
    {
        // Arrange
        using var client = new ScriptedChatClient();
        using var sut = Create(client, new FakeTool("read", isReadOnly: true));

        // Act
        var answered = sut.Approve("nope", ApprovalChoice.AllowOnce);

        // Assert
        answered.Should().BeFalse();
    }

    [Fact]
    public async Task Restore_ReplyWithToolCalls_RebuildsEntriesAndHistory()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("Looking"), ScriptedChatClient.Call("c1", "read", """{"a":1}""")])
            .Enqueue([ScriptedChatClient.Text("Found it")]);
        using var original = Create(client, new FakeTool("read", isReadOnly: true));
        await original.SendAsync("go", TestTargets.Create());
        using var sut = new ChatSession((_, _) => throw new InvalidOperationException());

        // Act
        sut.Restore(await _log.ReadAsync(TestContext.Current.CancellationToken));

        // Assert
        sut.Entries.Select(e => (e.Text, e.Status)).Should().Equal(original.Entries.Select(e => (e.Text, e.Status)));
        sut.Entries[1].ToolCalls.Single().Should().BeEquivalentTo(original.Entries[1].ToolCalls.Single());
        sut.History.Select(m => m.Role).Should().Equal(original.History.Select(m => m.Role));
        sut.History[1].Contents.OfType<FunctionCallContent>().Single().CallId.Should().Be("c1");
        sut.History[2].Contents.OfType<FunctionResultContent>().Single().Result.Should().Be("read done");
    }

    [Fact]
    public void Restore_CallWithoutResult_MarksCallAndReplyInterrupted()
    {
        // Arrange
        var user = Guid.NewGuid();
        var reply = Guid.NewGuid();
        using var sut = new ChatSession((_, _) => throw new InvalidOperationException());

        // Act
        sut.Restore(
        [
            new UserMessageEvent(Start, user, "go"),
            new AssistantStartEvent(Start, reply, "P1 · fast"),
            new ToolCallEvent(Start, reply, "c1", "write", "{}", "fake write"),
            new AssistantEndEvent(Start, reply, EntryStatus.Done, null, null),
        ]);

        // Assert
        sut.Entries[1].Status.Should().Be(EntryStatus.Interrupted);
        sut.Entries[1].ToolCalls.Single().Status.Should().Be(ToolCallStatus.Interrupted);
        sut.CanRetry.Should().BeTrue();
        sut.History[^1].Role.Should().Be(ChatRole.Tool);
        sut.History[^1].Contents.OfType<FunctionResultContent>().Single().CallId.Should().Be("c1");
    }

    [Fact]
    public async Task RetryAsync_InterruptedReplyWithCalls_KeepsItAndContinuesFromItsResults()
    {
        // Arrange
        var user = Guid.NewGuid();
        var reply = Guid.NewGuid();
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("fin")]);
        using var sut = Create(client, new FakeTool("write", isReadOnly: false));
        sut.Restore(
        [
            new UserMessageEvent(Start, user, "go"),
            new AssistantStartEvent(Start, reply, null),
            new ToolCallEvent(Start, reply, "c1", "write", "{}", string.Empty),
        ]);

        // Act
        await sut.RetryAsync(TestTargets.Create());

        // Assert
        _log.Events.OfType<RewindEvent>().Should().BeEmpty();
        sut.Entries.Select(e => e.Role.Value).Should().Equal("user", "assistant", "assistant");
        client.Requests.Single()[^1].Role.Should().Be(ChatRole.Tool);
    }

    [Fact]
    public async Task EditAsync_AfterAToolChain_DropsEveryEntryAndMessageAfterTheEditedOne()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read")])
            .Enqueue([ScriptedChatClient.Text("first answer")])
            .Enqueue([ScriptedChatClient.Text("second answer")]);
        using var sut = Create(client, new FakeTool("read", isReadOnly: true));
        await sut.SendAsync("go", TestTargets.Create());

        // Act
        await sut.EditAsync(sut.Entries[0].Id, "go again", TestTargets.Create());

        // Assert
        sut.Entries.Select(e => e.Text).Should().Equal("go again", "second answer");
        sut.History.Select(m => m.Role).Should().Equal(ChatRole.User, ChatRole.Assistant);
    }

    [Fact]
    public async Task SendAsync_ExtraTools_AreOfferedAndCalledForTheReply()
    {
        // Arrange
        var extra = new FakeTool("server__lookup", isReadOnly: false);
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "server__lookup")])
            .Enqueue([ScriptedChatClient.Text("done")]);
        using var sut = Create(client, new FakeTool("read", isReadOnly: true));
        sut.ExtraTools = _ => Task.FromResult<IReadOnlyList<ITool>>([extra]);
        sut.Permissions.Settings = new PermissionSettings(AccessLevel.WriteInFolder, ApprovalMode.NeverAsk);

        // Act
        await sut.SendAsync("go", TestTargets.Create());

        // Assert
        client.Options[0]!.Tools!.Select(t => t.Name).Should().Equal("read", "server__lookup");
        extra.Started.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_ExtraToolsAreAskedOncePerReply_NotOncePerRound()
    {
        // Arrange
        var asked = 0;
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read")])
            .Enqueue([ScriptedChatClient.Call("c2", "read")])
            .Enqueue([ScriptedChatClient.Text("done")]);
        using var sut = Create(client, new FakeTool("read", isReadOnly: true));
        sut.ExtraTools = _ =>
        {
            asked++;
            return Task.FromResult<IReadOnlyList<ITool>>([]);
        };

        // Act
        await sut.SendAsync("go", TestTargets.Create());
        await sut.SendAsync("again", TestTargets.Create());

        // Assert
        asked.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_ExtraToolsWithoutBaseTools_AreNotOffered()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("hi")]);
        using var sut = Create(client);
        sut.Tools = null;
        sut.ExtraTools = _ => Task.FromResult<IReadOnlyList<ITool>>([new FakeTool("extra", isReadOnly: true)]);

        // Act
        await sut.SendAsync("go", TestTargets.Create());

        // Assert
        client.Options[0]!.Tools.Should().BeNullOrEmpty();
    }

    private static async Task WaitForStatus(ChatSession session, ToolCallStatus status)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!session.Entries.Any(e => e.ToolCalls.Any(c => c.Status == status)))
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    private ChatSession Create(ScriptedChatClient client, params ITool[] tools)
    {
        return new ChatSession((_, _) => Task.FromResult<IChatClient>(client), _log, _time)
        {
            Tools = new ToolSet(tools, new ToolContext(Path.GetTempPath())),
        };
    }
}
