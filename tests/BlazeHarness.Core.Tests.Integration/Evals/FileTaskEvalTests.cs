using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Tests.Shared;
using BlazeHarness.Tests.Shared.Agent;

namespace BlazeHarness.Core.Tests.Integration.Evals;

/// <summary>Checks the end state of file tasks, which does not depend on the language of the reply.</summary>
public sealed class FileTaskEvalTests : IDisposable
{
    private readonly TempFolder _folder = new("bh-eval-");
    private readonly PermissionEngine _permissions = new() { Settings = new PermissionSettings(AccessLevel.WriteInFolder, ApprovalMode.AutoApprove) };

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task Run_ReadThenEdit_ChangesOnlyTheTargetedText()
    {
        // Arrange
        var config = _folder.Combine("app.cfg");
        await File.WriteAllTextAsync(config, "name=old\nmode=fast\n", TestContext.Current.CancellationToken);
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read_file", """{"path":"app.cfg"}""")])
            .Enqueue([ScriptedChatClient.Call("c2", "edit_file", """{"path":"app.cfg","old_string":"name=old","new_string":"name=new"}""")])
            .Enqueue([ScriptedChatClient.Text("ok")]);

        // Act
        var result = await AgentEvalRunner.RunAsync(_folder.Root, client, _permissions, approver: null, TestContext.Current.CancellationToken);

        // Assert
        (await File.ReadAllTextAsync(config, TestContext.Current.CancellationToken)).Should().Be("name=new\nmode=fast\n");
        result.ToolNames.Should().Equal("read_file", "edit_file");
        result.Statuses.Should().OnlyContain(s => s == ToolCallStatus.Succeeded);
        result.Stop.Should().Be(StopReason.Completed);
    }

    [Fact]
    public async Task Run_WriteNewFile_CreatesItWithTheContent()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "write_file", """{"path":"out/customers.csv","content":"name,email\nAda,ada@example.com\n"}""")])
            .Enqueue([ScriptedChatClient.Text("done")]);

        // Act
        var result = await AgentEvalRunner.RunAsync(_folder.Root, client, _permissions, approver: null, TestContext.Current.CancellationToken);

        // Assert
        (await File.ReadAllTextAsync(_folder.Combine("out", "customers.csv"), TestContext.Current.CancellationToken)).Should().Be("name,email\nAda,ada@example.com\n");
        result.Stop.Should().Be(StopReason.Completed);
    }

    [Fact]
    public async Task Run_EditWithTextThatIsNotThere_FailsTheCallAndLeavesTheFileAlone()
    {
        // Arrange
        var path = _folder.Combine("a.txt");
        await File.WriteAllTextAsync(path, "alpha", TestContext.Current.CancellationToken);
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "edit_file", """{"path":"a.txt","old_string":"beta","new_string":"gamma"}""")])
            .Enqueue([ScriptedChatClient.Text("could not")]);

        // Act
        var result = await AgentEvalRunner.RunAsync(_folder.Root, client, _permissions, approver: null, TestContext.Current.CancellationToken);

        // Assert
        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Should().Be("alpha");
        result.Statuses.Should().Equal(ToolCallStatus.Failed);
        result.Stop.Should().Be(StopReason.Completed);
    }
}
