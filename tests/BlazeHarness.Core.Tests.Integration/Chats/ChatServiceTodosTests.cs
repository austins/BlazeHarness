using BlazeHarness.Core.Agent;
using BlazeHarness.Tests.Shared.Agent;
using ChatService = BlazeHarness.Core.Chats.ChatService;

#pragma warning disable IDISP001 // The service disposes the chats it opens.

namespace BlazeHarness.Core.Tests.Integration.Chats;

public sealed class ChatServiceTodosTests : IDisposable
{
    private readonly ChatServiceRig _rig = new("bh-svc-todos-", "ok");
    private bool _todosEnabled = true;

    public ChatServiceTodosTests() => _rig.Build(todosEnabled: () => _todosEnabled);

    private ChatService Sut => _rig.Service;

    public void Dispose() => _rig.Dispose();

    [Fact]
    public async Task SendAsync_TodosOn_OffersTheTodoTool()
    {
        // Arrange
        var info = await Sut.CreateAsync(_rig.Work, TestContext.Current.CancellationToken);

        // Act
        await Sut.SendAsync(info.Id, "hi", TestTargets.Create("p", "m"), cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _rig.Client.Options[0]!.Tools!.Select(t => t.Name).Should().Contain(TodoList.ToolName);
    }

    [Fact]
    public async Task SendAsync_TodosTurnedOffBetweenTurns_DropsTheTodoTool()
    {
        // Arrange
        var info = await Sut.CreateAsync(_rig.Work, TestContext.Current.CancellationToken);
        await Sut.SendAsync(info.Id, "hi", TestTargets.Create("p", "m"), cancellationToken: TestContext.Current.CancellationToken);
        _rig.Client.Enqueue([ScriptedChatClient.Text("ok")]);
        _todosEnabled = false;

        // Act
        await Sut.SendAsync(info.Id, "again", TestTargets.Create("p", "m"), cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _rig.Client.Options[^1]!.Tools!.Select(t => t.Name).Should().NotContain(TodoList.ToolName).And.Contain("read_file");
    }
}
