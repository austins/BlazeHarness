using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Agent.Questions;
using BlazeHarness.Core.Chats;
using BlazeHarness.Core.Tools;
using BlazeHarness.Tests.Shared;
using BlazeHarness.Tests.Shared.Agent;
using BlazeHarness.Tests.Shared.Chats;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class ChatQuestionTests
{
    private const string Choice = """{"question":"Which one?","options":[{"label":"A"},{"label":"B"}]}""";
    private const string Multi = """{"question":"Which ones?","options":[{"label":"A"},{"label":"B"}],"multi_select":true}""";

    private readonly RecordingChatLog _log = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Answer_OfAWaitingQuestion_SavesASucceededResultAndContinues()
    {
        // Arrange
        using var client = Asking("c1", Choice);
        using var sut = Create(client);
        var turn = sut.SendAsync("go", TestTargets.Create());
        await WaitUntilWaiting(sut);
        sut.IsWaitingForUser.Should().BeTrue();
        sut.NeedsUser.Should().BeTrue();

        // Act
        var taken = sut.Answer("c1", UserAnswer.Chose(["B"]));
        await turn.Bounded();

        // Assert
        taken.Should().BeTrue();
        sut.IsWaitingForUser.Should().BeFalse();
        sut.NeedsUser.Should().BeFalse();
        var call = sut.Entries[1].ToolCalls.Single();
        (call.Status, call.Output).Should().Be((ToolCallStatus.Succeeded, "The user chose: \"B\"."));
        sut.Entries[^1].Text.Should().Be("thanks");
        _log.Events.OfType<ToolResultEvent>().Single().Status.Should().Be(ToolCallStatus.Succeeded);
    }

    [Fact]
    public async Task Answer_SecondAnswerToTheSameQuestion_ReturnsFalseAndKeepsTheFirst()
    {
        // Arrange
        using var client = Asking("c1", Choice);
        using var sut = Create(client);
        var turn = sut.SendAsync("go", TestTargets.Create());
        await WaitUntilWaiting(sut);

        // Act
        var first = sut.Answer("c1", UserAnswer.Chose(["A"]));
        var second = sut.Answer("c1", UserAnswer.Chose(["B"]));
        await turn.Bounded();

        // Assert
        (first, second).Should().Be((true, false));
        sut.Entries[1].ToolCalls.Single().Output.Should().Contain("\"A\"");
    }

    [Fact]
    public async Task Answer_NotOfferedOrTooManyOptions_IsRefusedAndTheQuestionKeepsWaiting()
    {
        // Arrange
        using var client = Asking("c1", Choice);
        using var sut = Create(client);
        var turn = sut.SendAsync("go", TestTargets.Create());
        await WaitUntilWaiting(sut);

        // Act
        var unknown = sut.Answer("c1", UserAnswer.Chose(["Z"]));
        var both = sut.Answer("c1", UserAnswer.Chose(["A", "B"]));
        var empty = sut.Answer("c1", UserAnswer.Answered("  "));
        var stillWaiting = sut.IsWaitingForUser;
        sut.Answer("c1", UserAnswer.Skip());
        await turn.Bounded();

        // Assert
        (unknown, both, empty, stillWaiting).Should().Be((false, false, false, true));
    }

    [Fact]
    public async Task Answer_SeveralOptionsOfAMultiSelectQuestion_AreAllTaken()
    {
        // Arrange
        using var client = Asking("c1", Multi);
        using var sut = Create(client);
        var turn = sut.SendAsync("go", TestTargets.Create());
        await WaitUntilWaiting(sut);

        // Act
        var taken = sut.Answer("c1", UserAnswer.Chose(["A", "B"], "both"));
        await turn.Bounded();

        // Assert
        taken.Should().BeTrue();
        sut.Entries[1].ToolCalls.Single().Output.Should().Be("The user chose: \"A\", \"B\". They added: both");
    }

    [Fact]
    public async Task Answer_VeryLongText_IsCutAtTheLimit()
    {
        // Arrange
        using var client = Asking("c1", """{"question":"Why?"}""");
        using var sut = Create(client);
        var turn = sut.SendAsync("go", TestTargets.Create());
        await WaitUntilWaiting(sut);

        // Act
        sut.Answer("c1", UserAnswer.Answered(new string('x', UserAnswer.MaxTextLength + 500)));
        await turn.Bounded();

        // Assert
        sut.Entries[1].ToolCalls.Single().Output.Should().HaveLength(AnswerText.AnsweredPrefix.Length + UserAnswer.MaxTextLength);
    }

    [Fact]
    public void Answer_NothingWaiting_ReturnsFalse()
    {
        // Arrange
        using var client = new ScriptedChatClient();
        using var sut = Create(client);

        // Act
        var taken = sut.Answer("c1", UserAnswer.Skip());

        // Assert
        taken.Should().BeFalse();
    }

    [Fact]
    public async Task IsWaitingForUser_AfterALongTime_IsStillTrue()
    {
        // Arrange
        using var client = Asking("c1", Choice);
        using var sut = Create(client);
        var turn = sut.SendAsync("go", TestTargets.Create());
        await WaitUntilWaiting(sut);

        // Act
        _time.Advance(Chat.ApprovalTimeout * 2);

        // Assert
        sut.IsWaitingForUser.Should().BeTrue();
        sut.Entries[1].ToolCalls.Single().Status.Should().Be(ToolCallStatus.AwaitingAnswer);
        sut.Stop();
        await turn.Bounded();
    }

    [Fact]
    public async Task Queue_WhileAQuestionWaits_BecomesTheAnswerAndIsNotQueued()
    {
        // Arrange
        using var client = Asking("c1", Choice);
        using var sut = Create(client);
        var turn = sut.SendAsync("go", TestTargets.Create());
        await WaitUntilWaiting(sut);

        // Act
        var accepted = sut.Queue("  neither, use C  ");
        await turn.Bounded();

        // Assert
        accepted.Should().BeTrue();
        sut.Queued.Should().BeEmpty();
        sut.Entries[1].ToolCalls.Single().Output.Should().Be("The user answered: neither, use C");
        sut.Entries.Select(e => e.Role).Should().Equal(ChatRole.User, ChatRole.Assistant, ChatRole.Assistant);
    }

    [Fact]
    public async Task Stop_WhileAQuestionWaits_CancelsTheCallWithTheStoppedNotice()
    {
        // Arrange
        using var client = Asking("c1", Choice);
        using var sut = Create(client);
        var turn = sut.SendAsync("go", TestTargets.Create());
        await WaitUntilWaiting(sut);

        // Act
        sut.Stop();
        await turn.Bounded();

        // Assert
        var call = sut.Entries[1].ToolCalls.Single();
        (call.Status, call.Output).Should().Be((ToolCallStatus.Cancelled, AnswerText.Stopped));
        sut.IsWaitingForUser.Should().BeFalse();
        sut.Answer("c1", UserAnswer.Skip()).Should().BeFalse();
    }

    [Fact]
    public async Task Restore_WhileAQuestionWaited_MarksItInterruptedWithTheQuestionNotice()
    {
        // Arrange
        using var client = Asking("c1", Choice);
        using var live = Create(client);
        var turn = live.SendAsync("go", TestTargets.Create());
        await WaitUntilWaiting(live);
        using var sut = new Chat((_, _) => throw new InvalidOperationException(), TimeProvider.System);

        // Act
        sut.Restore(_log.Events);

        // Assert
        var call = sut.Entries[1].ToolCalls.Single();
        (call.Status, call.Output).Should().Be((ToolCallStatus.Interrupted, AnswerText.Interrupted));
        call.Question!.Question.Should().Be("Which one?");
        sut.NeedsUser.Should().BeFalse();
        live.Stop();
        await turn.Bounded();
    }

    [Fact]
    public async Task Restore_AfterTheAnswer_EqualsTheLiveChat()
    {
        // Arrange
        using var client = Asking("c1", Choice);
        using var live = Create(client);
        var turn = live.SendAsync("go", TestTargets.Create());
        await WaitUntilWaiting(live);
        live.Answer("c1", UserAnswer.Chose(["A"]));
        await turn.Bounded();
        using var sut = new Chat((_, _) => throw new InvalidOperationException(), TimeProvider.System);

        // Act
        sut.Restore(_log.Events);

        // Assert
        sut.Entries[1].ToolCalls.Select(c => (c.Status, c.Output)).Should().Equal(live.Entries[1].ToolCalls.Select(c => (c.Status, c.Output)));
        sut.History.Select(m => m.Text).Should().Equal(live.History.Select(m => m.Text));
    }

    private static ScriptedChatClient Asking(string callId, string arguments)
    {
        return new ScriptedChatClient([ScriptedChatClient.Call(callId, UserQuestion.ToolName, arguments)])
            .Enqueue([ScriptedChatClient.Text("thanks")]);
    }

    private static async Task WaitUntilWaiting(Chat chat)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check(object? sender, EventArgs e)
        {
            if (chat.NeedsUser)
            {
                reached.TrySetResult();
            }
        }

        chat.Changed += Check;
        try
        {
            Check(chat, EventArgs.Empty);
            await reached.Task.WaitAsync(TestTimeouts.Default, TestContext.Current.CancellationToken);
        }
        finally
        {
            chat.Changed -= Check;
        }
    }

    private Chat Create(ScriptedChatClient client)
    {
        return new Chat((_, _) => Task.FromResult<IChatClient>(client), _time, _log)
        {
            Tools = new ToolSet([new AskUserTool()], new ToolContext(Path.GetTempPath()), TimeProvider.System),
        };
    }
}
