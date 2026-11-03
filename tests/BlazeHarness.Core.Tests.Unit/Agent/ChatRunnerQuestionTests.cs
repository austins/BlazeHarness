using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Agent.Events;
using BlazeHarness.Core.Agent.Questions;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Tools;
using BlazeHarness.Tests.Shared.Agent;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class ChatRunnerQuestionTests
{
    private const string Choice = """{"question":"Which one?","options":[{"label":"A"},{"label":"B"}]}""";
    private const string Open = """{"question":"Why?"}""";

    [Fact]
    public async Task RunAsync_QuestionAnswered_SavesTheAnswerAndContinues()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", UserQuestion.ToolName, Choice)])
            .Enqueue([ScriptedChatClient.Text("Going with A")]);
        var asker = FakeUserAsker.Always(UserAnswer.Chose(["A"]));
        var history = NewHistory();

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, history, TestContext.Current.CancellationToken, tools: Tools(), asker: asker);

        // Assert
        var expected = "The user chose: \"A\".";
        events.Select(e => e.GetType()).Should().StartWith(
            [typeof(ToolCallRequested), typeof(ToolAwaitingAnswer), typeof(ToolCallFinished), typeof(NewResponse), typeof(TextDelta), typeof(Done)]);
        events.OfType<ToolAwaitingAnswer>().Single().Question.Options.Select(o => o.Label).Should().Equal("A", "B");
        events.OfType<ToolCallFinished>().Single().Should().Be(new ToolCallFinished("c1", UserQuestion.ToolName, expected, ToolCallStatus.Succeeded));
        asker.Asked.Should().ContainSingle().Which.CallId.Should().Be("c1");
        history[2].Contents.OfType<FunctionResultContent>().Single().Result.Should().Be(expected);
    }

    [Fact]
    public async Task RunAsync_NoOneToAsk_FailsTheCallWithoutWaiting()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", UserQuestion.ToolName, Open)])
            .Enqueue([ScriptedChatClient.Text("ok")]);

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, NewHistory(), TestContext.Current.CancellationToken, tools: Tools());

        // Assert
        events.OfType<ToolAwaitingAnswer>().Should().BeEmpty();
        events.OfType<ToolCallFinished>().Should().ContainSingle().Which.Should().Be(
            new ToolCallFinished("c1", UserQuestion.ToolName, AskUserTool.NoAsker, ToolCallStatus.Failed));
    }

    [Fact]
    public async Task RunAsync_InvalidQuestion_FailsTheCallWithWhatToFix()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", UserQuestion.ToolName, """{"question":"Which?","options":["A"]}""")])
            .Enqueue([ScriptedChatClient.Text("ok")]);
        var asker = FakeUserAsker.Always(UserAnswer.Skip());

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, NewHistory(), TestContext.Current.CancellationToken, tools: Tools(), asker: asker);

        // Assert
        asker.Asked.Should().BeEmpty();
        events.OfType<ToolCallFinished>().Single().Status.Should().Be(ToolCallStatus.Failed);
        events.OfType<ToolCallFinished>().Single().Output.Should().Contain("option");
    }

    [Fact]
    public async Task RunAsync_StoppedWhileWaiting_SavesTheStoppedNoticeAndCancels()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", UserQuestion.ToolName, Open)]);
        var asker = new FakeUserAsker(async (_, _, ct) =>
        {
            await cts.CancelAsync();
            await Task.Delay(Timeout.Infinite, ct);
            return UserAnswer.Skip();
        });
        var history = NewHistory();

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, history, cts.Token, tools: Tools(), asker: asker);

        // Assert
        events.Should().ContainSingle(e => e is ToolCallFinished).Which.Should().Be(
            new ToolCallFinished("c1", UserQuestion.ToolName, AnswerText.Stopped, ToolCallStatus.Cancelled));
        events[^1].Should().Be(new Done(StopReason.Cancelled));
        history[^1].Contents.OfType<FunctionResultContent>().Single().Result.Should().Be(AnswerText.Stopped);
    }

    [Fact]
    public async Task RunAsync_TwoQuestionsInOneResponse_AsksTheFirstAndFailsTheSecond()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", UserQuestion.ToolName, Open), ScriptedChatClient.Call("c2", UserQuestion.ToolName, Open)])
            .Enqueue([ScriptedChatClient.Text("ok")]);
        var asker = FakeUserAsker.Always(UserAnswer.Answered("because"));

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, NewHistory(), TestContext.Current.CancellationToken, tools: Tools(), asker: asker);

        // Assert
        asker.Asked.Should().ContainSingle().Which.CallId.Should().Be("c1");
        events.OfType<ToolCallFinished>().Select(e => (e.CallId, e.Output, e.Status)).Should().Equal(
            ("c1", "The user answered: because", ToolCallStatus.Succeeded),
            ("c2", AskUserTool.OneAtATime, ToolCallStatus.Failed));
    }

    [Fact]
    public async Task RunAsync_FourthQuestionInARun_IsRefusedAndTheOtherThreeAreAsked()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", UserQuestion.ToolName, Open)])
            .Enqueue([ScriptedChatClient.Call("c2", UserQuestion.ToolName, Open)])
            .Enqueue([ScriptedChatClient.Call("c3", UserQuestion.ToolName, Open)])
            .Enqueue([ScriptedChatClient.Call("c4", UserQuestion.ToolName, Open)])
            .Enqueue([ScriptedChatClient.Text("fin")]);
        var asker = FakeUserAsker.Always(UserAnswer.Answered("x"));

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, NewHistory(), TestContext.Current.CancellationToken, tools: Tools(), asker: asker);

        // Assert
        asker.Asked.Select(a => a.CallId).Should().Equal("c1", "c2", "c3");
        events.OfType<ToolCallFinished>().Last().Should().Be(new ToolCallFinished("c4", UserQuestion.ToolName, AskUserTool.TooMany, ToolCallStatus.Failed));
    }

    [Theory]
    [InlineData(AccessLevel.ReadOnly)]
    [InlineData(AccessLevel.WriteInFolder)]
    [InlineData(AccessLevel.Unrestricted)]
    public async Task RunAsync_QuestionAtEveryAccessLevel_IsAskedWithoutApproval(AccessLevel access)
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", UserQuestion.ToolName, Open)])
            .Enqueue([ScriptedChatClient.Text("ok")]);
        var asker = FakeUserAsker.Always(UserAnswer.Skip());
        var permissions = new PermissionEngine { Settings = new PermissionSettings(access, ApprovalMode.AskFirst) };

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, NewHistory(), TestContext.Current.CancellationToken, tools: Tools(), permissions: permissions, asker: asker);

        // Assert
        events.OfType<ToolAwaitingApproval>().Should().BeEmpty();
        events.OfType<ToolCallFinished>().Single().Output.Should().Be(AnswerText.Skipped);
    }

    [Fact]
    public async Task RunAsync_QuestionBesideReads_IsNotBatchedWithThem()
    {
        // Arrange
        var read = new FakeTool("read", isReadOnly: true);
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read"), ScriptedChatClient.Call("c2", UserQuestion.ToolName, Open), ScriptedChatClient.Call("c3", "read")])
            .Enqueue([ScriptedChatClient.Text("ok")]);
        var asker = FakeUserAsker.Always(UserAnswer.LeaveToModel());

        // Act
        var events = await ChatRunnerCollector.CollectAsync(client, NewHistory(), TestContext.Current.CancellationToken, tools: Tools(read), asker: asker);

        // Assert
        events.OfType<ToolCallFinished>().Select(e => e.CallId).Should().Equal("c1", "c2", "c3");
        events.Select(e => e is ToolAwaitingAnswer ? "asked" : (e as ToolCallFinished)?.CallId).Where(x => x is not null).Should().Equal("c1", "asked", "c2", "c3");
        read.MaxConcurrent.Should().Be(1);
    }

    private static List<ChatMessage> NewHistory() => [new(ChatRole.User, "hi")];

    private static ToolSet Tools(params ITool[] others) => new([new AskUserTool(), .. others], new ToolContext(Path.GetTempPath()), TimeProvider.System);
}
