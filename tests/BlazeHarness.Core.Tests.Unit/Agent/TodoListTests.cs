using BlazeHarness.Core.Agent;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class TodoListTests
{
    private const string Items = """{"items":[{"text":"a","status":"completed"},{"text":"b","status":"in_progress"},{"text":"c","status":"pending"}]}""";

    [Fact]
    public void Current_AnItemInProgress_IsThatItem()
    {
        // Arrange
        var sut = new TodoList([new("a", TodoStatus.Pending), new("b", TodoStatus.InProgress)]);

        // Act
        var current = sut.Current;

        // Assert
        current!.Text.Should().Be("b");
    }

    [Fact]
    public void Current_NothingInProgress_IsTheFirstPendingItem()
    {
        // Arrange
        var sut = new TodoList([new("a", TodoStatus.Completed), new("b", TodoStatus.Pending), new("c", TodoStatus.Pending)]);

        // Act
        var current = sut.Current;

        // Assert
        current!.Text.Should().Be("b");
    }

    [Fact]
    public void Current_AllCompleted_IsNull()
    {
        // Arrange
        var sut = new TodoList([new("a", TodoStatus.Completed)]);

        // Act
        var current = sut.Current;

        // Assert
        current.Should().BeNull();
        sut.CompletedCount.Should().Be(1);
    }

    [Fact]
    public void From_Entries_UsesTheLastSucceededCall()
    {
        // Arrange
        var first = new ChatEntry(ChatRole.Assistant);
        first.AddToolCall(Call("c1", Items, ToolCallStatus.Succeeded));
        var second = new ChatEntry(ChatRole.Assistant);
        second.AddToolCall(Call("c2", """{"items":[{"text":"z","status":"pending"}]}""", ToolCallStatus.Succeeded));
        var third = new ChatEntry(ChatRole.Assistant);
        third.AddToolCall(Call("c3", """{"items":[{"text":"never","status":"pending"}]}""", ToolCallStatus.Failed));

        // Act
        var sut = TodoList.From([first, second, third]);

        // Assert
        sut.Items.Should().ContainSingle().Which.Text.Should().Be("z");
    }

    [Theory]
    [InlineData(ToolCallStatus.Failed)]
    [InlineData(ToolCallStatus.Denied)]
    [InlineData(ToolCallStatus.Cancelled)]
    [InlineData(ToolCallStatus.Interrupted)]
    [InlineData(ToolCallStatus.Pending)]
    public void From_Entries_IgnoresACallThatDidNotSucceed(ToolCallStatus status)
    {
        // Arrange
        var entry = new ChatEntry(ChatRole.Assistant);
        entry.AddToolCall(Call("c1", Items, status));

        // Act
        var sut = TodoList.From([entry]);

        // Assert
        sut.Items.Should().BeEmpty();
    }

    [Fact]
    public void From_Entries_IgnoresOtherToolsAndInvalidArguments()
    {
        // Arrange
        var entry = new ChatEntry(ChatRole.Assistant);
        entry.AddToolCall(new ToolCallEntry("c1", "read_file", Items, string.Empty) { Status = ToolCallStatus.Succeeded });
        entry.AddToolCall(Call("c2", "not json", ToolCallStatus.Succeeded));

        // Act
        var sut = TodoList.From([entry]);

        // Assert
        sut.Items.Should().BeEmpty();
    }

    [Fact]
    public void From_Messages_UsesTheLastCallThatSucceeded()
    {
        // Arrange
        List<ChatMessage> messages =
        [
            new(ChatRole.User, "go"),
            CallMessage("c1", """{"items":[{"text":"a","status":"pending"}]}"""),
            ResultMessage("c1", "Todo list updated: 0 of 1 done."),
            CallMessage("c2", """{"items":[{"text":"b","status":"pending"},{"text":"c","status":"pending"}]}"""),
            ResultMessage("c2", "Todo list updated: 0 of 2 done."),
            CallMessage("c3", """{"items":[{"text":"bad","status":"nope"}]}"""),
            ResultMessage("c3", "Error: item 1: 'status' must be one of pending, in_progress, completed."),
        ];

        // Act
        var sut = TodoList.From(messages);

        // Assert
        sut.Items.Select(i => i.Text).Should().Equal("b", "c");
    }

    [Fact]
    public void From_MessagesWithoutAnyUpdate_IsEmpty()
    {
        // Arrange
        List<ChatMessage> messages = [new(ChatRole.User, "go"), new(ChatRole.Assistant, "ok")];

        // Act
        var sut = TodoList.From(messages);

        // Assert
        sut.Items.Should().BeEmpty();
    }

    private static ToolCallEntry Call(string id, string json, ToolCallStatus status) =>
        new(id, TodoList.ToolName, json, string.Empty) { Status = status };

    private static ChatMessage CallMessage(string id, string json)
    {
        var arguments = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
        return new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(id, TodoList.ToolName, arguments)]);
    }

    private static ChatMessage ResultMessage(string id, string text) => new(ChatRole.Tool, [new FunctionResultContent(id, text)]);
}
