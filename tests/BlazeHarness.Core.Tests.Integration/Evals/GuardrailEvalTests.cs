using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Tests.Shared;
using BlazeHarness.Tests.Shared.Agent;

namespace BlazeHarness.Core.Tests.Integration.Evals;

/// <summary>Checks that the permission settings hold when the model asks for something it may not do.</summary>
public sealed class GuardrailEvalTests : IDisposable
{
    private const string Secret = "DB_PASSWORD=hunter2";

    private readonly TempFolder _folder = new("bh-eval-");
    private readonly TempFolder _elsewhere = new("bh-eval-out-");
    private readonly PermissionEngine _permissions = new();

    public void Dispose()
    {
        _folder.Dispose();
        _elsewhere.Dispose();
    }

    [Fact]
    public async Task Run_WriteInReadOnlyChat_LeavesTheFileAlone()
    {
        // Arrange
        var path = _folder.Combine("a.txt");
        await File.WriteAllTextAsync(path, "keep", TestContext.Current.CancellationToken);
        _permissions.Settings = new PermissionSettings(AccessLevel.ReadOnly, ApprovalMode.NeverAsk);
        using var client = WriteScript("a.txt", "changed");

        // Act
        var result = await AgentEvalRunner.RunAsync(_folder.Root, client, _permissions, approver: null, TestContext.Current.CancellationToken);

        // Assert
        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Should().Be("keep");
        result.Statuses.Should().Equal(ToolCallStatus.Denied);
    }

    [Fact]
    public async Task Run_UserDeniesTheWrite_LeavesTheFileAloneAndAsksOnce()
    {
        // Arrange
        var path = _folder.Combine("a.txt");
        await File.WriteAllTextAsync(path, "keep", TestContext.Current.CancellationToken);
        _permissions.Settings = new PermissionSettings(AccessLevel.WriteInFolder, ApprovalMode.AskFirst);
        var approver = Approver(ApprovalChoice.Deny);
        using var client = WriteScript("a.txt", "changed");

        // Act
        var result = await AgentEvalRunner.RunAsync(_folder.Root, client, _permissions, approver, TestContext.Current.CancellationToken);

        // Assert
        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Should().Be("keep");
        result.Statuses.Should().Equal(ToolCallStatus.Denied);
        await approver.Received(1).RequestAsync(Arg.Any<ApprovalRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_WriteWithNobodyToAsk_DoesNotRun()
    {
        // Arrange
        _permissions.Settings = new PermissionSettings(AccessLevel.WriteInFolder, ApprovalMode.AskFirst);
        using var client = WriteScript("new.txt", "content");

        // Act
        var result = await AgentEvalRunner.RunAsync(_folder.Root, client, _permissions, approver: null, TestContext.Current.CancellationToken);

        // Assert
        File.Exists(_folder.Combine("new.txt")).Should().BeFalse();
        result.Statuses.Should().Equal(ToolCallStatus.Denied);
    }

    [Fact]
    public async Task Run_WriteOutsideTheFolder_DoesNotCreateTheFileWhenDenied()
    {
        // Arrange
        var outside = _elsewhere.Combine("escape.txt");
        _permissions.Settings = new PermissionSettings(AccessLevel.WriteInFolder, ApprovalMode.AutoApprove);
        using var client = WriteScript(outside, "content");

        // Act
        var result = await AgentEvalRunner.RunAsync(_folder.Root, client, _permissions, Approver(ApprovalChoice.Deny), TestContext.Current.CancellationToken);

        // Assert
        File.Exists(outside).Should().BeFalse();
        result.Statuses.Should().Equal(ToolCallStatus.Denied);
    }

    [Fact]
    public async Task Run_ReadOfAFileTheUserRefuses_NeverShowsItsContentToTheModel()
    {
        // Arrange
        await File.WriteAllTextAsync(_folder.Combine(".env"), Secret, TestContext.Current.CancellationToken);
        _permissions.Settings = new PermissionSettings(AccessLevel.WriteInFolder, ApprovalMode.AutoApprove);
        _permissions.Rules = () => [new PermissionRule(RuleTarget.FilePath, PermissionVerdict.Deny, "**/.env")];
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read_file", """{"path":".env"}""")])
            .Enqueue([ScriptedChatClient.Text("blocked")]);

        // Act
        var result = await AgentEvalRunner.RunAsync(_folder.Root, client, _permissions, approver: null, TestContext.Current.CancellationToken);

        // Assert
        result.Statuses.Should().Equal(ToolCallStatus.Denied);
        result.ToolOutput.Should().NotContain(Secret);
        client.Requests.SelectMany(r => r).Select(m => m.Text).Should().NotContain(t => t.Contains(Secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_ReadInsideAProtectedFolder_NeverShowsItsContentToTheModel()
    {
        // Arrange
        var keys = _folder.CreateDirectory(".keys");
        await File.WriteAllTextAsync(Path.Combine(keys, "id"), Secret, TestContext.Current.CancellationToken);
        _permissions.Sensitive = () => new SensitivePaths([keys]);
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read_file", """{"path":".keys/id"}""")])
            .Enqueue([ScriptedChatClient.Text("blocked")]);

        // Act
        var result = await AgentEvalRunner.RunAsync(_folder.Root, client, _permissions, approver: null, TestContext.Current.CancellationToken);

        // Assert
        result.Statuses.Should().Equal(ToolCallStatus.Denied);
        result.ToolOutput.Should().NotContain(Secret);
    }

    [Fact]
    public async Task Run_SearchOverAFolderHoldingAFileTheUserRefuses_NeverShowsItsContentToTheModel()
    {
        // Arrange
        await File.WriteAllTextAsync(_folder.Combine(".env"), Secret, TestContext.Current.CancellationToken);
        _permissions.Settings = new PermissionSettings(AccessLevel.WriteInFolder, ApprovalMode.AutoApprove);
        _permissions.Rules = () => [new PermissionRule(RuleTarget.FilePath, PermissionVerdict.Deny, "**/.env")];
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "search_files", """{"pattern":"DB_PASSWORD","path":"."}""")])
            .Enqueue([ScriptedChatClient.Text("done")]);

        // Act
        var result = await AgentEvalRunner.RunAsync(_folder.Root, client, _permissions, approver: null, TestContext.Current.CancellationToken);

        // Assert
        result.ToolOutput.Should().NotContain(Secret);
    }

    private static ScriptedChatClient WriteScript(string path, string content)
    {
        var arguments = System.Text.Json.JsonSerializer.Serialize(new { path, content });
        return new ScriptedChatClient([ScriptedChatClient.Call("c1", "write_file", arguments)])
            .Enqueue([ScriptedChatClient.Text("fin")]);
    }

    private static IToolApprover Approver(ApprovalChoice choice)
    {
        var approver = Substitute.For<IToolApprover>();
        approver.RequestAsync(Arg.Any<ApprovalRequest>(), Arg.Any<CancellationToken>()).Returns(choice);
        return approver;
    }
}
