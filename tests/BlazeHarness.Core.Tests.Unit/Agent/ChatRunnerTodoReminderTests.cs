using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Tools;
using BlazeHarness.Tests.Shared.Agent;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class ChatRunnerTodoReminderTests
{
    private const string OpenList = """{"items":[{"text":"a","status":"in_progress"},{"text":"b","status":"pending"}]}""";
    private const string DoneList = """{"items":[{"text":"a","status":"completed"}]}""";

    [Fact]
    public async Task RunAsync_OpenListUnchangedForTheLimit_AddsTheReminderOnce()
    {
        // Arrange
        using var client = Rounds(OpenList, ChatRunner.TodoReminderCalls).Enqueue([ScriptedChatClient.Text("done")]);
        var history = NewHistory();

        // Act
        await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken, tools: Tools());

        // Assert
        history.Count(m => m.Text == ChatRunner.TodoReminder).Should().Be(1);
        history[^2].Text.Should().Be(ChatRunner.TodoReminder);
        history[^2].Role.Should().Be(ChatRole.User);
        client.Requests[^1][^1].Text.Should().Be(ChatRunner.TodoReminder);
    }

    [Fact]
    public async Task RunAsync_OneFewerCalls_AddsNoReminder()
    {
        // Arrange
        using var client = Rounds(OpenList, ChatRunner.TodoReminderCalls - 1).Enqueue([ScriptedChatClient.Text("done")]);
        var history = NewHistory();

        // Act
        await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken, tools: Tools());

        // Assert
        history.Should().NotContain(m => m.Text == ChatRunner.TodoReminder);
    }

    [Fact]
    public async Task RunAsync_EveryItemCompleted_AddsNoReminder()
    {
        // Arrange
        using var client = Rounds(DoneList, ChatRunner.TodoReminderCalls).Enqueue([ScriptedChatClient.Text("done")]);
        var history = NewHistory();

        // Act
        await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken, tools: Tools());

        // Assert
        history.Should().NotContain(m => m.Text == ChatRunner.TodoReminder);
    }

    [Fact]
    public async Task RunAsync_ListUpdatedInBetween_ResetsTheCount()
    {
        // Arrange
        using var client = Rounds(OpenList, ChatRunner.TodoReminderCalls - 1);
        client.Enqueue([ScriptedChatClient.Call("t2", TodoList.ToolName, OpenList)]);
        for (var i = 0; i < ChatRunner.TodoReminderCalls - 1; i++)
        {
            client.Enqueue([ScriptedChatClient.Call($"w{i}", "work")]);
        }

        client.Enqueue([ScriptedChatClient.Text("done")]);
        var history = NewHistory();

        // Act
        await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken, tools: Tools());

        // Assert
        history.Should().NotContain(m => m.Text == ChatRunner.TodoReminder);
    }

    [Fact]
    public async Task RunAsync_WithoutTheTodoTool_AddsNoReminder()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("w0", "work")]);
        for (var i = 1; i < ChatRunner.TodoReminderCalls; i++)
        {
            client.Enqueue([ScriptedChatClient.Call($"w{i}", "work")]);
        }

        client.Enqueue([ScriptedChatClient.Text("done")]);
        var history = NewHistory();

        // Act
        await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken, tools: Tools(withTodos: false));

        // Assert
        history.Should().NotContain(m => m.Text == ChatRunner.TodoReminder);
    }

    // One update_todos round that sets the list, then the given number of rounds with one other tool call each.
    private static ScriptedChatClient Rounds(string list, int workCalls)
    {
        var client = new ScriptedChatClient([ScriptedChatClient.Call("t1", TodoList.ToolName, list)]);
        for (var i = 0; i < workCalls; i++)
        {
            client.Enqueue([ScriptedChatClient.Call($"c{i}", "work")]);
        }

        return client;
    }

    private static List<ChatMessage> NewHistory() => [new(ChatRole.User, "hi")];

    private static ToolSet Tools(bool withTodos = true)
    {
        var work = new FakeTool("work", isReadOnly: true, (_, _) => Task.FromResult(ToolResult.Ok("ok")));
        ITool[] tools = withTodos ? [work, new TodoTool()] : [work];
        return new ToolSet(tools, new ToolContext(Path.GetTempPath()), TimeProvider.System);
    }
}
