using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Tools;
using BlazeHarness.Tests.Shared.Agent;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class ChatRunnerFailureTests
{
    [Fact]
    public async Task RunAsync_EveryRoundFails_AddsTheNoticeAfterTheLimit()
    {
        // Arrange
        var fail = new FakeTool("fail", isReadOnly: true, (_, _) => Task.FromResult(ToolResult.Error("no")));
        using var client = FailingRounds(ChatRunner.MaxFailedRounds).Enqueue([ScriptedChatClient.Text("done")]);
        var history = NewHistory();

        // Act
        await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken, tools: Tools(fail));

        // Assert
        history.Count(m => HasNotice(m, ChatRunner.FailureNotice)).Should().Be(1);
        history[^2].Role.Should().Be(ChatRole.Tool);
        HasNotice(history[^2], ChatRunner.FailureNotice).Should().BeTrue();
        HasNotice(client.Requests[^1][^1], ChatRunner.FailureNotice).Should().BeTrue();
        history.Should().ContainSingle(m => m.Role == ChatRole.User);
    }

    [Fact]
    public async Task RunAsync_OneFewerFailedRounds_AddsNoNotice()
    {
        // Arrange
        var fail = new FakeTool("fail", isReadOnly: true, (_, _) => Task.FromResult(ToolResult.Error("no")));
        using var client = FailingRounds(ChatRunner.MaxFailedRounds - 1).Enqueue([ScriptedChatClient.Text("done")]);
        var history = NewHistory();

        // Act
        await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken, tools: Tools(fail));

        // Assert
        history.Should().NotContain(m => HasNotice(m, ChatRunner.FailureNotice));
    }

    [Fact]
    public async Task RunAsync_ASuccessBetweenFailures_ResetsTheCount()
    {
        // Arrange
        var results = new Queue<ToolResult>(
            Enumerable.Repeat(ToolResult.Error("no"), ChatRunner.MaxFailedRounds - 1)
                .Append(ToolResult.Ok("fine"))
                .Concat(Enumerable.Repeat(ToolResult.Error("no"), ChatRunner.MaxFailedRounds - 1)));
        var tool = new FakeTool("flaky", isReadOnly: true, (_, _) => Task.FromResult(results.Dequeue()));
        using var client = FailingRounds((ChatRunner.MaxFailedRounds * 2) - 1, "flaky").Enqueue([ScriptedChatClient.Text("done")]);
        var history = NewHistory();

        // Act
        await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken, tools: Tools(tool));

        // Assert
        history.Should().NotContain(m => HasNotice(m, ChatRunner.FailureNotice));
    }

    [Fact]
    public async Task RunAsync_MessageQueuedAtTheLimit_TakesThePlaceOfTheNotice()
    {
        // Arrange
        var fail = new FakeTool("fail", isReadOnly: true, (_, _) => Task.FromResult(ToolResult.Error("no")));
        using var client = FailingRounds(ChatRunner.MaxFailedRounds).Enqueue([ScriptedChatClient.Text("done")]);
        var history = NewHistory();
        var inbox = new FakeRunInbox([.. new string?[ChatRunner.MaxFailedRounds - 1], "try this"]);

        // Act
        await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken, tools: Tools(fail), inbox: inbox);

        // Assert
        history.Should().NotContain(m => HasNotice(m, ChatRunner.FailureNotice));
        history[^2].Text.Should().Be("try this");
    }

    private static ScriptedChatClient FailingRounds(int count, string tool = "fail")
    {
        var client = new ScriptedChatClient([ScriptedChatClient.Call("c0", tool)]);
        for (var i = 1; i < count; i++)
        {
            client.Enqueue([ScriptedChatClient.Call($"c{i}", tool)]);
        }

        return client;
    }

    private static bool HasNotice(ChatMessage message, string notice) =>
        message.Contents.OfType<FunctionResultContent>().Any(r => r.Result is string text && text.EndsWith(ChatRunner.NoticePrefix + notice, StringComparison.Ordinal));

    private static List<ChatMessage> NewHistory() => [new(ChatRole.User, "hi")];

    private static ToolSet Tools(params ITool[] tools) => new(tools, new ToolContext(Path.GetTempPath()), TimeProvider.System);
}
