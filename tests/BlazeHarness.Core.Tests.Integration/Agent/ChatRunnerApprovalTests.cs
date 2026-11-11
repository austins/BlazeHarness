using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Agent.Events;
using BlazeHarness.Core.Diff;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Tools;
using BlazeHarness.Core.Tools.Files;
using BlazeHarness.Tests.Shared;
using BlazeHarness.Tests.Shared.Agent;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Integration.Agent;

public sealed class ChatRunnerApprovalTests : IDisposable
{
    private readonly TempFolder _temp = new("approval-");

    public void Dispose() => _temp.Dispose();

    [Theory(Skip = SymlinkSupport.Reason, SkipUnless = nameof(SymlinkSupport.CanCreate), SkipType = typeof(SymlinkSupport))]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAsync_LinkRetargetedDuringApproval_DoesNotReadEitherTarget(bool protectTarget)
    {
        // Arrange
        var root = _temp.CreateDirectory("work");
        var original = _temp.Combine("original.txt");
        var replacement = _temp.Combine("replacement.txt");
        var link = Path.Combine(root, "link.txt");
        await File.WriteAllTextAsync(original, "original content", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(replacement, "private content", TestContext.Current.CancellationToken);
        File.CreateSymbolicLink(link, original);
        var permissions = new PermissionEngine
        {
            Sensitive = () => protectTarget ? new SensitivePaths([replacement]) : SensitivePaths.None,
        };
        var approver = Substitute.For<IToolApprover>();
        approver.RequestAsync(Arg.Any<ApprovalRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            File.Delete(link);
            File.CreateSymbolicLink(link, replacement);
            return Task.FromResult(ApprovalChoice.AllowForChat);
        });
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("call", "read_file", """{"path":"link.txt"}""")])
            .Enqueue([ScriptedChatClient.Text("done")]);
        var tools = new ToolSet([new ReadFileTool()], new ToolContext(root), TimeProvider.System);
        List<ChatMessage> history = [new(ChatRole.User, "Read the file.")];
        List<AgentEvent> events = [];

        // Act
        await foreach (var item in ChatRunner.RunAsync(client, history, new ChatOptions(), TimeProvider.System, tools, approver, permissions, cancellationToken: TestContext.Current.CancellationToken))
        {
            events.Add(item);
        }

        // Assert
        var result = events.OfType<ToolCallFinished>().Single();
        result.Status.Should().Be(ToolCallStatus.Denied);
        result.Output.Should().NotContain("private content").And.NotContain("original content");
        permissions.Evaluate(new ReadFileTool(), """{"path":"../original.txt"}""", new ToolContext(root)).Verdict.Should().Be(PermissionVerdict.Ask);
    }

    [Fact]
    public async Task RunAsync_WriteFileNeedingApproval_PreviewsTheChangeBeforeAsking()
    {
        // Arrange
        var root = _temp.CreateDirectory("work");
        await File.WriteAllTextAsync(Path.Combine(root, "notes.txt"), "keep\nold", TestContext.Current.CancellationToken);
        var approver = Substitute.For<IToolApprover>();
        approver.RequestAsync(Arg.Any<ApprovalRequest>(), Arg.Any<CancellationToken>()).Returns(ApprovalChoice.Deny);

        // Act
        var events = await RunWriteAsync(root, new PermissionEngine(), approver);

        // Assert
        var preview = events.OfType<ToolChangePreview>().Should().ContainSingle().Subject;
        preview.Lines.Should().Equal(
            new DiffLine(DiffKind.Same, "keep"),
            new DiffLine(DiffKind.Removed, "old"),
            new DiffLine(DiffKind.Added, "new"));
        events.IndexOf(preview).Should().BeLessThan(events.FindIndex(e => e is ToolAwaitingApproval));
        (await File.ReadAllTextAsync(Path.Combine(root, "notes.txt"), TestContext.Current.CancellationToken)).Should().Be("keep\nold");
    }

    [Fact]
    public async Task RunAsync_WriteFileToAProtectedFile_HasNoPreview()
    {
        // Arrange
        var root = _temp.CreateDirectory("work");
        var target = Path.Combine(root, "notes.txt");
        await File.WriteAllTextAsync(target, "private", TestContext.Current.CancellationToken);
        var permissions = new PermissionEngine { Sensitive = () => new SensitivePaths([target]) };
        var approver = Substitute.For<IToolApprover>();
        approver.RequestAsync(Arg.Any<ApprovalRequest>(), Arg.Any<CancellationToken>()).Returns(ApprovalChoice.Deny);

        // Act
        var events = await RunWriteAsync(root, permissions, approver);

        // Assert
        events.OfType<ToolChangePreview>().Should().BeEmpty();
    }

    private static async Task<List<AgentEvent>> RunWriteAsync(string root, PermissionEngine permissions, IToolApprover approver)
    {
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("call", "write_file", """{"path":"notes.txt","content":"keep\nnew"}""")])
            .Enqueue([ScriptedChatClient.Text("done")]);
        var tools = new ToolSet([new WriteFileTool()], new ToolContext(root), TimeProvider.System);
        List<ChatMessage> history = [new(ChatRole.User, "Edit the file.")];
        List<AgentEvent> events = [];
        await foreach (var item in ChatRunner.RunAsync(client, history, new ChatOptions(), TimeProvider.System, tools, approver, permissions, cancellationToken: TestContext.Current.CancellationToken))
        {
            events.Add(item);
        }

        return events;
    }
}
