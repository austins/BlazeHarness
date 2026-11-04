using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Tools;
using BlazeHarness.Tests.Shared;
using BlazeHarness.Tests.Shared.Agent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class ChatHostTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task SendAsync_CommandReachingAHost_WaitsForTheUserThenRuns()
    {
        // Arrange
        var command = new ConnectingTool("example.com");
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", ConnectingTool.ToolName, """{"command":"curl example.com"}""")])
            .Enqueue([ScriptedChatClient.Text("done")]);
        using var sut = Create(client, command);
        var turn = sut.SendAsync("go", TestTargets.Create());
        await WaitForAsync(sut, () => sut.Entries.Any(e => e.ToolCalls.Any(c => c.Status == ToolCallStatus.AwaitingApproval)));
        sut.Approve("c1", ApprovalChoice.AllowOnce).Should().BeTrue();
        await WaitForAsync(sut, () => sut.Entries.Any(e => e.ToolCalls.Any(c => c.PendingHosts.Count > 0)));
        sut.NeedsUser.Should().BeTrue();
        var call = sut.Entries.SelectMany(e => e.ToolCalls).Single();
        call.PendingHosts.Should().Equal(new PendingHost("example.com", CanRemember: true));

        // Act
        var taken = sut.ApproveHost("c1", "example.com", ApprovalChoice.AllowForChat);
        await turn.Bounded();

        // Assert
        taken.Should().BeTrue();
        call.PendingHosts.Should().BeEmpty();
        call.Output.Should().Be("allowed");
        sut.Permissions.EvaluateNetworkHost("example.com").Verdict.Should().Be(PermissionVerdict.Allow);
    }

    [Fact]
    public async Task SendAsync_CommandHostDenied_TheCommandSeesTheRefusal()
    {
        // Arrange
        var command = new ConnectingTool("example.com");
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", ConnectingTool.ToolName, """{"command":"curl example.com"}""")])
            .Enqueue([ScriptedChatClient.Text("done")]);
        using var sut = Create(client, command);
        var turn = sut.SendAsync("go", TestTargets.Create());
        await WaitForAsync(sut, () => sut.Entries.Any(e => e.ToolCalls.Any(c => c.Status == ToolCallStatus.AwaitingApproval)));
        sut.Approve("c1", ApprovalChoice.AllowOnce);
        await WaitForAsync(sut, () => sut.Entries.Any(e => e.ToolCalls.Any(c => c.PendingHosts.Count > 0)));

        // Act
        sut.ApproveHost("c1", "example.com", ApprovalChoice.Deny);
        await turn.Bounded();

        // Assert
        sut.Entries.SelectMany(e => e.ToolCalls).Single().Output.Should().Be("refused");
        sut.Permissions.EvaluateNetworkHost("example.com").Verdict.Should().Be(PermissionVerdict.Ask);
    }

    [Fact]
    public async Task SendAsync_FilterOff_GivesTheCommandNoGate()
    {
        // Arrange
        var command = new ConnectingTool("example.com");
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", ConnectingTool.ToolName, """{"command":"curl example.com"}""")])
            .Enqueue([ScriptedChatClient.Text("done")]);
        using var sut = Create(client, command);
        sut.Permissions.NetworkFilter = () => false;
        var turn = sut.SendAsync("go", TestTargets.Create());
        await WaitForAsync(sut, () => sut.Entries.Any(e => e.ToolCalls.Any(c => c.Status == ToolCallStatus.AwaitingApproval)));

        // Act
        sut.Approve("c1", ApprovalChoice.AllowOnce);
        await turn.Bounded();

        // Assert
        sut.Entries.SelectMany(e => e.ToolCalls).Single().Output.Should().Be("no gate");
    }

    [Fact]
    public void ApproveHost_NothingWaiting_ReturnsFalse()
    {
        // Arrange
        using var client = new ScriptedChatClient([]);
        using var sut = Create(client);

        // Act
        var taken = sut.ApproveHost("c1", "example.com", ApprovalChoice.AllowOnce);

        // Assert
        taken.Should().BeFalse();
    }

    private static async Task WaitForAsync(Chat chat, Func<bool> condition)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check(object? sender, EventArgs e)
        {
            if (condition())
            {
                reached.TrySetResult();
            }
        }

        chat.Changed += Check;
        try
        {
            Check(chat, EventArgs.Empty);
            await reached.Task.Bounded();
        }
        finally
        {
            chat.Changed -= Check;
        }
    }

    private Chat Create(ScriptedChatClient client, params ITool[] tools)
    {
        var chat = new Chat((_, _) => Task.FromResult<IChatClient>(client), _time)
        {
            Tools = new ToolSet(tools, new ToolContext(Path.GetTempPath()), TimeProvider.System),
        };
        chat.Permissions.NetworkFilter = () => true;
        chat.Permissions.Sandbox = new SandboxStatus(true, "fake", "A fake sandbox.");
        return chat;
    }

    private sealed class ConnectingTool(string host) : ITool
    {
        public const string ToolName = "run";

        public string Name => ToolName;

        public string Description => "Runs a command that connects to a host.";

        public string InputSchema => """{"type":"object","properties":{"command":{"type":"string"}}}""";

        public bool IsReadOnly => false;

        public bool RunsCommands => true;

        public TimeSpan Timeout => TestTimeouts.Default;

        public string Summarize(JsonElement arguments) => "run";

        public async Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
        {
            if (context.Network is not { } gate)
            {
                return ToolResult.Ok("no gate");
            }

            var access = await gate.RequestAsync(host, cancellationToken);
            return ToolResult.Ok(access.Allowed ? "allowed" : "refused");
        }
    }
}
