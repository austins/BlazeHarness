using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Tools;
using BlazeHarness.Tests.Shared;
using BlazeHarness.Tests.Shared.Agent;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class ChatRunnerToolTests
{
    [Fact]
    public async Task RunAsync_ReadOnlyCall_RunsItAndAnswersInSecondRound()
    {
        // Arrange
        var read = new FakeTool("read", isReadOnly: true);
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read", """{"a":1}""")])
            .Enqueue([ScriptedChatClient.Text("All done")]);
        var history = NewHistory();

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken, tools: Tools(read));

        // Assert
        events.Should().Equal(
            new ToolCallRequested("c1", "read", """{"a":1}""", "fake read"),
            new ToolCallRunning("c1"),
            new ToolCallFinished("c1", "read", "read done", ToolCallStatus.Succeeded),
            new NewResponse(),
            new TextDelta("All done"),
            new Done(StopReason.Completed));
        history.Select(m => m.Role).Should().Equal(ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant);
        history[1].Contents.OfType<FunctionCallContent>().Should().ContainSingle().Which.CallId.Should().Be("c1");
        history[2].Contents.OfType<FunctionResultContent>().Should().ContainSingle().Which.Result.Should().Be("read done");
        client.Options[0]!.Tools.Should().ContainSingle().Which.Name.Should().Be("read");
    }

    [Fact]
    public async Task RunAsync_TwoReadOnlyCalls_RunThemAtTheSameTime()
    {
        // Arrange
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var read = new FakeTool("read", isReadOnly: true, async (_, ct) =>
        {
            if (Interlocked.Increment(ref started) == 2)
            {
                bothStarted.SetResult();
            }

            await bothStarted.Task.WaitAsync(TestTimeouts.Default, ct);
            return ToolResult.Ok("ok");
        });
        using var client = new ScriptedChatClient(
                [ScriptedChatClient.Call("c1", "read"), ScriptedChatClient.Call("c2", "read")])
            .Enqueue([ScriptedChatClient.Text("fin")]);

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, NewHistory(), TestContext.Current.CancellationToken, tools: Tools(read));

        // Assert
        read.MaxConcurrent.Should().Be(2);
        events.OfType<ToolCallFinished>().Select(e => e.Status).Should().Equal(ToolCallStatus.Succeeded, ToolCallStatus.Succeeded);
    }

    [Fact]
    public async Task RunAsync_ReadThenWriteThenRead_AsksOnlyForTheWriteAndKeepsOrder()
    {
        // Arrange
        var read = new FakeTool("read", isReadOnly: true);
        var write = new FakeTool("write", isReadOnly: false);
        var approver = Approver(ApprovalChoice.AllowOnce);
        using var client = new ScriptedChatClient(
                [ScriptedChatClient.Call("c1", "read"), ScriptedChatClient.Call("c2", "write"), ScriptedChatClient.Call("c3", "read")])
            .Enqueue([ScriptedChatClient.Text("fin")]);
        var history = NewHistory();

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken, tools: Tools(read, write), approver: approver);

        // Assert
        await approver.Received(1).RequestAsync(Arg.Any<ApprovalRequest>(), Arg.Any<CancellationToken>());
        await approver.Received(1).RequestAsync(Arg.Is<ApprovalRequest>(r => r.ToolName == "write"), Arg.Any<CancellationToken>());
        events.OfType<ToolCallFinished>().Select(e => e.CallId).Should().Equal("c1", "c2", "c3");
        events.OfType<ToolAwaitingApproval>().Should().ContainSingle().Which.CallId.Should().Be("c2");
        events.Should().ContainInOrder(events.OfType<ToolAwaitingApproval>().Single(), new ToolCallRunning("c2"));
        history[2].Contents.OfType<FunctionResultContent>().Select(r => r.CallId).Should().Equal("c1", "c2", "c3");
    }

    [Fact]
    public async Task RunAsync_ApprovalDenied_DoesNotRunAndTellsTheModel()
    {
        // Arrange
        var write = new FakeTool("write", isReadOnly: false);
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "write")])
            .Enqueue([ScriptedChatClient.Text("ok, skipping")]);
        var history = NewHistory();

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken, tools: Tools(write), approver: Approver(ApprovalChoice.Deny));

        // Assert
        write.Started.Should().Be(0);
        events.OfType<ToolCallFinished>().Should().ContainSingle().Which.Status.Should().Be(ToolCallStatus.Denied);
        history[2].Contents.OfType<FunctionResultContent>().Single().Result!.ToString().Should().Contain("denied");
    }

    [Fact]
    public async Task RunAsync_NoApprover_DeniesCallThatIsNotReadOnly()
    {
        // Arrange
        var write = new FakeTool("write", isReadOnly: false);
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "write")])
            .Enqueue([ScriptedChatClient.Text("fin")]);

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, NewHistory(), TestContext.Current.CancellationToken, tools: Tools(write));

        // Assert
        write.Started.Should().Be(0);
        events.OfType<ToolCallFinished>().Single().Status.Should().Be(ToolCallStatus.Denied);
        events.OfType<ToolAwaitingApproval>().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_UnknownTool_FailsTheCallAndListsTheTools()
    {
        // Arrange
        var read = new FakeTool("read", isReadOnly: true);
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "nope")])
            .Enqueue([ScriptedChatClient.Text("fin")]);
        var history = NewHistory();

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken, tools: Tools(read));

        // Assert
        var finished = events.OfType<ToolCallFinished>().Single();
        finished.Status.Should().Be(ToolCallStatus.Failed);
        finished.Output.Should().Contain("nope").And.Contain("read");
        history[2].Contents.OfType<FunctionResultContent>().Single().Result!.ToString().Should().StartWith("Error: ");
    }

    [Fact]
    public async Task RunAsync_ToolReturnsError_MarksTheCallFailed()
    {
        // Arrange
        var read = new FakeTool("read", isReadOnly: true, (_, _) => Task.FromResult(ToolResult.Error("no such file")));
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read")])
            .Enqueue([ScriptedChatClient.Text("fin")]);

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, NewHistory(), TestContext.Current.CancellationToken, tools: Tools(read));

        // Assert
        events.OfType<ToolCallFinished>().Single().Should().Be(new ToolCallFinished("c1", "read", "no such file", ToolCallStatus.Failed));
    }

    [Fact]
    public async Task RunAsync_ToolThrows_MarksTheCallFailedAndContinues()
    {
        // Arrange
        var read = new FakeTool("read", isReadOnly: true, (_, _) => throw new InvalidOperationException("kaput"));
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read")])
            .Enqueue([ScriptedChatClient.Text("fin")]);

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, NewHistory(), TestContext.Current.CancellationToken, tools: Tools(read));

        // Assert
        events.OfType<ToolCallFinished>().Single().Output.Should().Contain("kaput");
        events[^1].Should().Be(new Done(StopReason.Completed));
    }

    [Fact]
    public async Task RunAsync_NoLimitGiven_KeepsCallingPastFiftyRounds()
    {
        // Arrange
        var read = new FakeTool("read", isReadOnly: true);
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c0", "read")]);
        for (var i = 1; i < 60; i++)
        {
            client.Enqueue([ScriptedChatClient.Call("c" + i, "read")]);
        }

        client.Enqueue([ScriptedChatClient.Text("done")]);

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, NewHistory(), TestContext.Current.CancellationToken, tools: Tools(read));

        // Assert
        events[^1].Should().Be(new Done(StopReason.Completed));
        client.Requests.Should().HaveCount(61);
    }

    [Fact]
    public async Task RunAsync_ReplyIsOnlyATextToolCall_TellsTheModelAndAsksAgain()
    {
        // Arrange
        var read = new FakeTool("read", isReadOnly: true);
        using var client = new ScriptedChatClient(
                [ScriptedChatClient.Text("<tool_call><function=read><parameter=path>a</parameter></function></tool_call>")])
            .Enqueue([ScriptedChatClient.Call("c1", "read")])
            .Enqueue([ScriptedChatClient.Text("fin")]);
        var history = NewHistory();

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken, tools: Tools(read));

        // Assert
        events[^1].Should().Be(new Done(StopReason.Completed));
        events.OfType<ToolCallFinished>().Should().ContainSingle();
        history.Select(m => m.Role).Should().Equal(
            ChatRole.User, ChatRole.Assistant, ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant);
        history[2].Text.Should().Contain("tool interface");
    }

    [Fact]
    public async Task RunAsync_ModelKeepsWritingTextToolCalls_GivesUpAfterThreeNotices()
    {
        // Arrange
        var read = new FakeTool("read", isReadOnly: true);
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("<tool_call>x")]);
        for (var i = 0; i < 4; i++)
        {
            client.Enqueue([ScriptedChatClient.Text("<tool_call>x")]);
        }

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, NewHistory(), TestContext.Current.CancellationToken, tools: Tools(read));

        // Assert
        events[^1].Should().Be(new Done(StopReason.Completed));
        client.Requests.Should().HaveCount(4);
    }

    [Fact]
    public async Task RunAsync_CancelledWhileToolRuns_AnswersEveryCallInHistory()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var read = new FakeTool("read", isReadOnly: true);
        var write = new FakeTool("write", isReadOnly: false, async (_, ct) =>
        {
            await cts.CancelAsync();
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return ToolResult.Ok("never");
        });
        using var client = new ScriptedChatClient(
            [ScriptedChatClient.Call("c1", "read"), ScriptedChatClient.Call("c2", "write"), ScriptedChatClient.Call("c3", "read")]);
        var history = NewHistory();

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, history, cts.Token, tools: Tools(read, write), approver: Approver(ApprovalChoice.AllowOnce));

        // Assert
        events.OfType<ToolCallFinished>().Select(e => (e.CallId, e.Status)).Should().Equal(
            ("c1", ToolCallStatus.Succeeded),
            ("c2", ToolCallStatus.Cancelled),
            ("c3", ToolCallStatus.Cancelled));
        events[^1].Should().Be(new Done(StopReason.Cancelled));
        history[^1].Contents.OfType<FunctionResultContent>().Select(r => r.CallId).Should().Equal("c1", "c2", "c3");
        client.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_WriteWhenReadOnly_DeniesWithoutAsking()
    {
        // Arrange
        var write = new FakeTool("write", isReadOnly: false);
        var approver = Approver(ApprovalChoice.AllowOnce);
        var permissions = new PermissionEngine { Settings = new PermissionSettings(AccessLevel.ReadOnly, ApprovalMode.AskFirst) };
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "write")])
            .Enqueue([ScriptedChatClient.Text("fin")]);
        var history = NewHistory();

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken, tools: Tools(write), approver: approver, permissions: permissions);

        // Assert
        write.Started.Should().Be(0);
        await approver.DidNotReceive().RequestAsync(Arg.Any<ApprovalRequest>(), Arg.Any<CancellationToken>());
        events.OfType<ToolCallFinished>().Single().Status.Should().Be(ToolCallStatus.Denied);
        history[2].Contents.OfType<FunctionResultContent>().Single().Result!.ToString().Should().Contain("Read only");
    }

    [Fact]
    public async Task RunAsync_WriteWhenAutoApprove_RunsWithoutAsking()
    {
        // Arrange
        var write = new FakeTool("write", isReadOnly: false);
        var approver = Approver(ApprovalChoice.Deny);
        var permissions = new PermissionEngine { Settings = new PermissionSettings(AccessLevel.WriteInFolder, ApprovalMode.AutoApprove) };
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "write")])
            .Enqueue([ScriptedChatClient.Text("fin")]);

        // Act
        await ChatRunnerCollector.CollectAsync(client, NewHistory(), TestContext.Current.CancellationToken, tools: Tools(write), approver: approver, permissions: permissions);

        // Assert
        write.Started.Should().Be(1);
        await approver.DidNotReceive().RequestAsync(Arg.Any<ApprovalRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_AllowedForTheSession_AsksOnlyOnce()
    {
        // Arrange
        var write = new FakeTool("write", isReadOnly: false);
        var approver = Approver(ApprovalChoice.AllowForSession);
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "write")])
            .Enqueue([ScriptedChatClient.Call("c2", "write")])
            .Enqueue([ScriptedChatClient.Text("fin")]);

        // Act
        await ChatRunnerCollector.CollectAsync(client, NewHistory(), TestContext.Current.CancellationToken, tools: Tools(write), approver: approver);

        // Assert
        write.Started.Should().Be(2);
        await approver.Received(1).RequestAsync(Arg.Is<ApprovalRequest>(r => r.CanRemember), Arg.Any<CancellationToken>());
        await approver.Received(1).RequestAsync(Arg.Any<ApprovalRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_AllowForSessionWithoutAGrantKey_IsNotRemembered()
    {
        // Arrange
        var write = new FakeTool("write", isReadOnly: false, pathArguments: ["path"]);
        var approver = Approver(ApprovalChoice.AllowForSession);
        var permissions = new PermissionEngine { Rules = () => [new PermissionRule(RuleTarget.FilePath, PermissionVerdict.Ask, "*")] };
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "write", """{"path":"a.txt"}""")])
            .Enqueue([ScriptedChatClient.Call("c2", "write", """{"path":"a.txt"}""")])
            .Enqueue([ScriptedChatClient.Text("fin")]);

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, NewHistory(), TestContext.Current.CancellationToken, tools: Tools(write), approver: approver, permissions: permissions);

        // Assert
        write.Started.Should().Be(2);
        await approver.Received(2).RequestAsync(Arg.Is<ApprovalRequest>(r => !r.CanRemember), Arg.Any<CancellationToken>());
        events.OfType<ToolAwaitingApproval>().Should().OnlyContain(e => !e.CanRemember);
    }

    [Fact]
    public async Task RunAsync_DeniedOnce_StillAsksAgainForALaterCall()
    {
        // Arrange
        var write = new FakeTool("write", isReadOnly: false);
        var approver = Approver(ApprovalChoice.Deny);
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "write")])
            .Enqueue([ScriptedChatClient.Call("c2", "write")])
            .Enqueue([ScriptedChatClient.Text("fin")]);

        // Act
        await ChatRunnerCollector.CollectAsync(client, NewHistory(), TestContext.Current.CancellationToken, tools: Tools(write), approver: approver);

        // Assert
        write.Started.Should().Be(0);
        await approver.Received(2).RequestAsync(Arg.Any<ApprovalRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_AllowedForTheSession_IsNotSharedWithAnotherEngine()
    {
        // Arrange
        var write = new FakeTool("write", isReadOnly: false);
        var approver = Approver(ApprovalChoice.AllowForSession);
        using var first = new ScriptedChatClient([ScriptedChatClient.Call("c1", "write")]).Enqueue([ScriptedChatClient.Text("fin")]);
        using var second = new ScriptedChatClient([ScriptedChatClient.Call("c2", "write")]).Enqueue([ScriptedChatClient.Text("fin")]);

        // Act
        await ChatRunnerCollector.CollectAsync(first, NewHistory(), TestContext.Current.CancellationToken, tools: Tools(write), approver: approver, permissions: new PermissionEngine());
        await ChatRunnerCollector.CollectAsync(second, NewHistory(), TestContext.Current.CancellationToken, tools: Tools(write), approver: approver, permissions: new PermissionEngine());

        // Assert
        await approver.Received(2).RequestAsync(Arg.Any<ApprovalRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_AllowedOnce_AsksAgainNextTime()
    {
        // Arrange
        var write = new FakeTool("write", isReadOnly: false);
        var approver = Approver(ApprovalChoice.AllowOnce);
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "write")])
            .Enqueue([ScriptedChatClient.Call("c2", "write")])
            .Enqueue([ScriptedChatClient.Text("fin")]);

        // Act
        await ChatRunnerCollector.CollectAsync(client, NewHistory(), TestContext.Current.CancellationToken, tools: Tools(write), approver: approver);

        // Assert
        await approver.Received(2).RequestAsync(Arg.Is<ApprovalRequest>(r => r.ToolName == "write"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_AskedWithAReason_TellsTheUser()
    {
        // Arrange
        var write = new FakeTool("write", isReadOnly: false);
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "write")])
            .Enqueue([ScriptedChatClient.Text("fin")]);

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, NewHistory(), TestContext.Current.CancellationToken, tools: Tools(write), approver: Approver(ApprovalChoice.AllowOnce));

        // Assert
        var awaiting = events.OfType<ToolAwaitingApproval>().Single();
        awaiting.Reason.Should().NotBeEmpty();
        awaiting.CanRemember.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_CancelledWhileAwaitingApproval_CancelsTheCall()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var write = new FakeTool("write", isReadOnly: false);
        var approver = Substitute.For<IToolApprover>();
        approver.RequestAsync(Arg.Any<ApprovalRequest>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            await cts.CancelAsync();
            await Task.Delay(TimeSpan.FromSeconds(10), cts.Token);
            return ApprovalChoice.AllowOnce;
        });
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "write")]);
        var history = NewHistory();

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, history, cts.Token, tools: Tools(write), approver: approver);

        // Assert
        write.Started.Should().Be(0);
        events.OfType<ToolCallFinished>().Single().Status.Should().Be(ToolCallStatus.Cancelled);
        events[^1].Should().Be(new Done(StopReason.Cancelled));
        history[^1].Role.Should().Be(ChatRole.Tool);
    }

    [Fact]
    public async Task RunAsync_ModelFailsAfterCalls_DropsTheCalls()
    {
        // Arrange
        var read = new FakeTool("read", isReadOnly: true);
        using var client = new ScriptedChatClient(
            [ScriptedChatClient.Text("part"), ScriptedChatClient.Call("c1", "read")],
            new InvalidOperationException("boom"));
        var history = NewHistory();

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken, tools: Tools(read));

        // Assert
        events.Should().NotContain(e => e is ToolCallRequested);
        events[^1].Should().Be(new Done(StopReason.Error));
        history.Should().HaveCount(2);
        history[1].Contents.OfType<FunctionCallContent>().Should().BeEmpty();
        history[1].Text.Should().Be("part");
    }

    private static IToolApprover Approver(ApprovalChoice choice)
    {
        var approver = Substitute.For<IToolApprover>();
        approver.RequestAsync(Arg.Any<ApprovalRequest>(), Arg.Any<CancellationToken>()).Returns(choice);
        return approver;
    }

    private static List<ChatMessage> NewHistory() => [new(ChatRole.User, "hi")];

    private static ToolSet Tools(params ITool[] tools) => new(tools, new ToolContext(Path.GetTempPath()));
}
