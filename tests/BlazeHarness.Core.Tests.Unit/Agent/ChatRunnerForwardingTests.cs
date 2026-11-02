using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Agent.Events;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Tools;
using BlazeHarness.Tests.Shared;
using BlazeHarness.Tests.Shared.Agent;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class ChatRunnerForwardingTests
{
    [Fact]
    public async Task RunAsync_ForwardedCall_RunsTheRealToolWithItsOwnArguments()
    {
        // Arrange
        string? seen = null;
        var real = new FakeTool("srv__read", isReadOnly: true, (args, _) =>
        {
            seen = args.RequiredString("path");
            return Task.FromResult(ToolResult.Ok("contents"));
        });
        var forwarder = new ForwardingTool(new ForwardedCall(real, """{"path":"a.txt"}""", "srv__read", null));
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "use_tool", """{"name":"srv__read","arguments":{"path":"a.txt"}}""")])
            .Enqueue([ScriptedChatClient.Text("fin")]);
        var history = NewHistory();

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken, tools: Tools(forwarder));

        // Assert
        seen.Should().Be("a.txt");
        events.OfType<ToolCallRequested>().Single().Summary.Should().StartWith("srv__read");
        events.OfType<ToolCallFinished>().Single().Status.Should().Be(ToolCallStatus.Succeeded);
        history[1].Contents.OfType<FunctionCallContent>().Single().Name.Should().Be("use_tool");
        history[2].Contents.OfType<FunctionResultContent>().Single().Result.Should().Be("contents");
    }

    [Fact]
    public async Task RunAsync_ForwardedCallToAWriteTool_AsksAboutTheRealTool()
    {
        // Arrange
        var real = new FakeTool("srv__write", isReadOnly: false);
        var forwarder = new ForwardingTool(new ForwardedCall(real, "{}", "srv__write", null));
        var approver = Substitute.For<IToolApprover>();
        approver.RequestAsync(Arg.Any<ApprovalRequest>(), Arg.Any<CancellationToken>()).Returns(ApprovalChoice.Deny);
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "use_tool", """{"name":"srv__write"}""")])
            .Enqueue([ScriptedChatClient.Text("fin")]);

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, NewHistory(), TestContext.Current.CancellationToken, tools: Tools(forwarder), approver: approver);

        // Assert
        await approver.Received(1).RequestAsync(Arg.Is<ApprovalRequest>(r => r.ToolName == "srv__write"), Arg.Any<CancellationToken>());
        real.Started.Should().Be(0);
        events.OfType<ToolCallFinished>().Single().Status.Should().Be(ToolCallStatus.Denied);
    }

    [Fact]
    public async Task RunAsync_ForwardedCallThatDoesNotResolve_FailsWithTheProblem()
    {
        // Arrange
        var forwarder = new ForwardingTool(new ForwardedCall(null, "{}", "srv__nope", "There is no tool named 'srv__nope'."));
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "use_tool", """{"name":"srv__nope"}""")])
            .Enqueue([ScriptedChatClient.Text("fin")]);

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, NewHistory(), TestContext.Current.CancellationToken, tools: Tools(forwarder));

        // Assert
        var finished = events.OfType<ToolCallFinished>().Single();
        finished.Status.Should().Be(ToolCallStatus.Failed);
        finished.Output.Should().Contain("There is no tool named 'srv__nope'.");
    }

    private static List<ChatMessage> NewHistory() => [new(ChatRole.User, "hi")];

    private static ToolSet Tools(params ITool[] tools) => new(tools, new ToolContext(Path.GetTempPath()), TimeProvider.System);

    private sealed class ForwardingTool(ForwardedCall call) : IForwardingTool
    {
        public string Name => "use_tool";

        public string Description => "Forwards.";

        public string InputSchema => """{"type":"object","properties":{}}""";

        public bool IsReadOnly => false;

        public TimeSpan Timeout => TestTimeouts.Default;

        public string Summarize(JsonElement arguments) => "forward";

        public Task<ForwardedCall> ResolveAsync(ToolArguments arguments, CancellationToken cancellationToken) => Task.FromResult(call);

        public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
            => Task.FromResult(ToolResult.Error("not resolved"));
    }
}
