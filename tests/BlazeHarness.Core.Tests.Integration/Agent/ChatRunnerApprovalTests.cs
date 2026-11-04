using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Agent.Events;
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

    public static bool IsWindows => OperatingSystem.IsWindows();

    public void Dispose() => _temp.Dispose();

    [Theory(Skip = "Creating Unix symlinks requires no special privileges.", SkipWhen = nameof(IsWindows))]
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
}
