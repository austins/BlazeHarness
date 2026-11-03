using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Tools;
using BlazeHarness.Tests.Shared.Agent;
using BlazeHarness.Tests.Shared.Chats;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class ChatTodosTests
{
    private const string FirstList = """{"items":[{"text":"read","status":"completed"},{"text":"edit","status":"in_progress"}]}""";
    private const string SecondList = """{"items":[{"text":"read","status":"completed"},{"text":"edit","status":"completed"},{"text":"test","status":"pending"}]}""";

    private readonly RecordingChatLog _log = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Todos_NewChat_IsEmpty()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("hi")]);
        using var sut = Create(client);

        // Act
        var todos = sut.Todos;

        // Assert
        todos.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Todos_AfterTwoUpdates_IsTheLastOne()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", TodoList.ToolName, FirstList)])
            .Enqueue([ScriptedChatClient.Call("c2", TodoList.ToolName, SecondList)])
            .Enqueue([ScriptedChatClient.Text("done")]);
        using var sut = Create(client);

        // Act
        await sut.SendAsync("go", TestTargets.Create());

        // Assert
        sut.Todos.Items.Select(i => i.Text).Should().Equal("read", "edit", "test");
        sut.Todos.CompletedCount.Should().Be(2);
        sut.Todos.Current!.Text.Should().Be("test");
    }

    [Fact]
    public async Task Todos_AFailedUpdate_KeepsTheEarlierList()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", TodoList.ToolName, FirstList)])
            .Enqueue([ScriptedChatClient.Call("c2", TodoList.ToolName, """{"items":[{"text":"x","status":"bogus"}]}""")])
            .Enqueue([ScriptedChatClient.Text("done")]);
        using var sut = Create(client);

        // Act
        await sut.SendAsync("go", TestTargets.Create());

        // Assert
        sut.Entries.SelectMany(e => e.ToolCalls).Last().Status.Should().Be(ToolCallStatus.Failed);
        sut.Todos.Items.Select(i => i.Text).Should().Equal("read", "edit");
    }

    [Fact]
    public async Task Todos_AfterEditingTheMessageThatSetThem_IsEmptyAgain()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", TodoList.ToolName, FirstList)])
            .Enqueue([ScriptedChatClient.Text("done")])
            .Enqueue([ScriptedChatClient.Text("different answer")]);
        using var sut = Create(client);
        await sut.SendAsync("go", TestTargets.Create());
        sut.Todos.Items.Should().HaveCount(2);

        // Act
        await sut.EditAsync(sut.Entries[0].Id, "go again", TestTargets.Create());

        // Assert
        sut.Todos.Items.Should().BeEmpty();
    }

    private Chat Create(ScriptedChatClient client)
    {
        return new Chat((_, _) => Task.FromResult<IChatClient>(client), _time, _log)
        {
            Tools = new ToolSet([new TodoTool()], new ToolContext(Path.GetTempPath()), TimeProvider.System),
        };
    }
}
