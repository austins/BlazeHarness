using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Compaction;
using BlazeHarness.Core.Tools;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class ChatRunnerCompactionTests
{
    private readonly ISummarizer _summarizer = Substitute.For<ISummarizer>();

    public ChatRunnerCompactionTests()
    {
        _summarizer.SummarizeAsync(Arg.Any<string?>(), Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>()).Returns("the summary");
    }

    [Fact]
    public async Task RunAsync_OverTheLimitBeforeTheFirstCall_CompactsAndSendsTheSummary()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        var history = Conversation(10);
        var context = new ConversationContext(1_000, _summarizer);

        // Act
        var events = await Collect(client, history, context, TestContext.Current.CancellationToken);

        // Assert
        events.Should().HaveCount(3);
        events[0].Should().BeOfType<ContextCompacted>().Which.FirstKept.Should().Be(8);
        events[2].Should().Be(new Done(StopReason.Completed));
        var sent = client.Requests.Single();
        sent.Should().HaveCount(3);
        sent[0].Text.Should().Contain("the summary");
        sent[1].Should().BeSameAs(history[8]);
        history.Should().HaveCount(11);
    }

    [Fact]
    public async Task RunAsync_UnderTheLimit_SendsEverythingAndNeverSummarizes()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        var context = new ConversationContext(100_000, _summarizer);

        // Act
        var events = await Collect(client, Conversation(10), context, TestContext.Current.CancellationToken);

        // Assert
        events.OfType<ContextCompacted>().Should().BeEmpty();
        client.Requests.Single().Should().HaveCount(10);
        await _summarizer.DidNotReceiveWithAnyArgs().SummarizeAsync(null, [], TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunAsync_UsageGrowsDuringTheRun_CompactsBeforeTheNextCallAndBeforeItsNewResponse()
    {
        // Arrange
        var read = new FakeTool("read", isReadOnly: true);
        var usage = new UsageContent(new UsageDetails { InputTokenCount = 5_000, OutputTokenCount = 10 });
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read"), new ChatResponseUpdate(ChatRole.Assistant, [usage])])
            .Enqueue([ScriptedChatClient.Text("fin")]);
        var history = Conversation(6);
        var context = new ConversationContext(1_000, _summarizer);

        // Act
        var events = await Collect(client, history, context, TestContext.Current.CancellationToken, new ToolSet([read], new ToolContext(Path.GetTempPath())));

        // Assert
        var types = events.Select(e => e.GetType()).ToList();
        types.IndexOf(typeof(ContextCompacted)).Should().BeLessThan(types.IndexOf(typeof(NewResponse)));
        events.OfType<ContextCompacted>().Should().ContainSingle();
        client.Requests[1][0].Text.Should().Contain("the summary");
        client.Requests[1][^1].Role.Should().Be(ChatRole.Tool);
    }

    [Fact]
    public async Task RunAsync_WhenSummarizingFails_ReportsItAndCarriesOnWithTheFullHistory()
    {
        // Arrange
        _summarizer.SummarizeAsync(Arg.Any<string?>(), Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>()).Returns<string>(_ => throw new InvalidOperationException("no summary"));
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        var context = new ConversationContext(1_000, _summarizer);

        // Act
        var events = await Collect(client, Conversation(10), context, TestContext.Current.CancellationToken);

        // Assert
        events[0].Should().Be(new ContextCompactionFailed("no summary"));
        events[^1].Should().Be(new Done(StopReason.Completed));
        client.Requests.Single().Should().HaveCount(10);
    }

    [Fact]
    public async Task RunAsync_StoppedBeforeSummarizingFinishes_EndsAsCancelled()
    {
        // Arrange
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();
        _summarizer.SummarizeAsync(Arg.Any<string?>(), Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns<string>(call => throw new OperationCanceledException(call.Arg<CancellationToken>()));
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        var context = new ConversationContext(1_000, _summarizer);

        // Act
        var events = await Collect(client, Conversation(10), context, stop.Token);

        // Assert
        events.Should().Equal(new Done(StopReason.Cancelled));
        client.Requests.Should().BeEmpty();
    }

    // Messages that alternate between user and assistant, ending with the user's, at about 100 tokens each.
    private static List<ChatMessage> Conversation(int count)
    {
        return [.. Enumerable.Range(0, count).Select(i => new ChatMessage(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, new string('x', 400)))];
    }

    private static async Task<List<AgentEvent>> Collect(
        IChatClient client,
        IList<ChatMessage> history,
        ConversationContext context,
        CancellationToken cancellationToken,
        ToolSet? tools = null)
    {
        var events = new List<AgentEvent>();
        await foreach (var e in ChatRunner.RunAsync(client, history, new ChatOptions(), tools, context: context, cancellationToken: cancellationToken))
        {
            events.Add(e);
        }

        return events;
    }
}
