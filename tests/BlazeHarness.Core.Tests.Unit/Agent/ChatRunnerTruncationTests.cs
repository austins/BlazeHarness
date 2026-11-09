using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Agent.Events;
using BlazeHarness.Core.Tools;
using BlazeHarness.Tests.Shared.Agent;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class ChatRunnerTruncationTests
{
    [Fact]
    public async Task RunAsync_CallInAReplyCutOff_RefusesItAndAsksAgain()
    {
        // Arrange
        var tool = new FakeTool("write", isReadOnly: true);
        using var client = new ScriptedChatClient(CutOff("c0"))
            .Enqueue([ScriptedChatClient.Text("done")]);
        var history = NewHistory();

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken, tools: Tools(tool));

        // Assert
        tool.Started.Should().Be(0);
        events.Should().NotContain(e => e is ResponseTruncated);
        events.Should().ContainSingle(e => e is ToolCallFinished)
            .Which.Should().BeOfType<ToolCallFinished>().Which.Status.Should().Be(ToolCallStatus.Failed);
        client.Requests.Should().HaveCount(2);
        history[2].Contents.OfType<FunctionResultContent>().Single().Result.Should().BeOfType<string>()
            .Which.Should().Contain(ChatRunner.TruncatedCallError);
        events[^1].Should().Be(new Done(StopReason.Completed));
    }

    [Fact]
    public async Task RunAsync_EveryReplyCutOff_StopsAfterTheRetriesWithTheWarning()
    {
        // Arrange
        var tool = new FakeTool("write", isReadOnly: true);
        using var client = new ScriptedChatClient(CutOff("c0"));
        for (var i = 1; i <= ChatRunner.MaxTruncatedRetries; i++)
        {
            client.Enqueue(CutOff($"c{i}"));
        }

        var history = NewHistory();

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken, tools: Tools(tool));

        // Assert
        tool.Started.Should().Be(0);
        client.Requests.Should().HaveCount(ChatRunner.MaxTruncatedRetries + 1);
        events.Should().ContainSingle(e => e is ResponseTruncated);
        events[^1].Should().Be(new Done(StopReason.Completed));
        history[^1].Role.Should().Be(ChatRole.Tool);
    }

    private static ChatResponseUpdate[] CutOff(string callId) =>
        [ScriptedChatClient.Call(callId, "write", """{"path":"a"}"""), new ChatResponseUpdate { FinishReason = ChatFinishReason.Length }];

    private static List<ChatMessage> NewHistory() => [new(ChatRole.User, "hi")];

    private static ToolSet Tools(params ITool[] tools) => new(tools, new ToolContext(Path.GetTempPath()), TimeProvider.System);
}
