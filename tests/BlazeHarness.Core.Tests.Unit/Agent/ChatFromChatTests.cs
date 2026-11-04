using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Agent.Questions;
using BlazeHarness.Core.Chats.Events;
using BlazeHarness.Core.Tools;
using BlazeHarness.Tests.Shared;
using BlazeHarness.Tests.Shared.Agent;
using BlazeHarness.Tests.Shared.Chats;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class ChatFromChatTests
{
    private const string Choice = """{"question":"Which one?","options":[{"label":"A"},{"label":"B"}]}""";

    private static readonly ChatSource Sender = new("abc123", "Story 1", 1);

    private readonly RecordingChatLog _log = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task QueueFromChat_NoReplyRunning_ReturnsFalse()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        using var sut = Create(client);
        await sut.SendAsync("hi", TestTargets.Create());

        // Act
        var accepted = sut.QueueFromChat("late", Sender);

        // Assert
        accepted.Should().BeFalse();
        sut.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task StartFromChat_ReplyRunning_DoesNotStart()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read")]).Enqueue([ScriptedChatClient.Text("fin")]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sut = Create(client, HoldingTools(started, release));
        var turn = sut.SendAsync("hi", TestTargets.Create());
        await started.Task.Bounded();

        // Act
        var (begun, _) = sut.StartFromChat("from", Sender, TestTargets.Create());
        release.SetResult();
        await turn.Bounded();

        // Assert
        begun.Should().BeFalse();
        sut.Entries.Count(e => e.Role == ChatRole.User).Should().Be(1);
    }

    [Fact]
    public async Task QueueFromChat_WhileAToolRuns_AddsItAfterTheReplyAsItsOwnMessageWithItsSource()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read")])
            .Enqueue([ScriptedChatClient.Text("fin")])
            .Enqueue([ScriptedChatClient.Text("after")]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sut = Create(client, HoldingTools(started, release));
        var turn = sut.SendAsync("hi", TestTargets.Create());
        await started.Task.Bounded();

        // Act
        var accepted = sut.QueueFromChat("passed along", Sender);
        var queuedForTheUser = sut.Queued.ToList();
        release.SetResult();
        await turn.Bounded();

        // Assert
        accepted.Should().BeTrue();
        queuedForTheUser.Should().BeEmpty();
        var users = sut.Entries.Where(e => e.Role == ChatRole.User).ToList();
        users.Select(e => (e.Text, e.Origin, e.Source)).Should().Equal(("hi", null, null), ("passed along", UserMessageOrigin.FromChat, Sender));
        sut.Entries[^1].Text.Should().Be("after");
        client.Requests[^1].Select(m => m.Text).Should().Contain("passed along");
        _log.Events.OfType<UserMessageEvent>().Select(e => (e.Origin, e.Source)).Should().Equal((null, null), (UserMessageOrigin.FromChat, Sender));
    }

    [Fact]
    public async Task QueueFromChat_WithAUserMessageQueued_KeepsThemAsSeparateMessages()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read")])
            .Enqueue([ScriptedChatClient.Text("fin")])
            .Enqueue([ScriptedChatClient.Text("two")])
            .Enqueue([ScriptedChatClient.Text("three")]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sut = Create(client, HoldingTools(started, release));
        var turn = sut.SendAsync("hi", TestTargets.Create());
        await started.Task.Bounded();

        // Act
        sut.Queue("my own note", QueueMode.AfterReply);
        sut.QueueFromChat("passed along", Sender);
        release.SetResult();
        await turn.Bounded();

        // Assert
        var users = sut.Entries.Where(e => e.Role == ChatRole.User).ToList();
        users.Select(e => (e.Text, e.Source)).Should().Equal(("hi", null), ("my own note", null), ("passed along", Sender));
        users[1].Origin.Should().NotBe(UserMessageOrigin.FromChat);
        users[2].Origin.Should().Be(UserMessageOrigin.FromChat);
    }

    [Fact]
    public async Task QueueFromChat_ReplyStopped_AddsTheMessageWithoutAReplyAndNeverHandsItBack()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read")]).Enqueue([ScriptedChatClient.Text("ok")]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sut = Create(client, HoldingTools(started, release));
        var turn = sut.SendAsync("hi", TestTargets.Create());
        await started.Task.Bounded();
        sut.QueueFromChat("passed along", Sender);

        // Act
        sut.Stop();
        await turn.Bounded();
        var handedBack = sut.TakeQueued();

        // Assert
        handedBack.Should().BeEmpty();
        sut.IsRunning.Should().BeFalse();
        client.Requests.Should().ContainSingle();
        sut.Entries[^1].Should().Match<ChatEntry>(e => e.Role == ChatRole.User && e.Origin == UserMessageOrigin.FromChat && e.Source == Sender && e.Text == "passed along");
        sut.CanRetry.Should().BeTrue();
        _log.Events.OfType<UserMessageEvent>().Last().Source.Should().Be(Sender);
    }

    [Fact]
    public async Task QueueFromChat_ReplyStopped_RetryAnswersTheMessage()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read")]).Enqueue([ScriptedChatClient.Text("ok")]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sut = Create(client, HoldingTools(started, release));
        var turn = sut.SendAsync("hi", TestTargets.Create());
        await started.Task.Bounded();
        sut.QueueFromChat("passed along", Sender);
        sut.Stop();
        await turn.Bounded();

        // Act
        await sut.RetryAsync(TestTargets.Create());

        // Assert
        client.Requests[^1].Select(m => m.Text).Should().Contain("passed along");
        sut.Entries[^1].Text.Should().Be("ok");
    }

    [Fact]
    public async Task QueueFromChat_WhileAQuestionWaits_DoesNotAnswerIt()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", UserQuestion.ToolName, Choice)])
            .Enqueue([ScriptedChatClient.Text("thanks")])
            .Enqueue([ScriptedChatClient.Text("got it")]);
        using var sut = Create(client, new ToolSet([new AskUserTool()], new ToolContext(Path.GetTempPath()), TimeProvider.System));
        var turn = sut.SendAsync("go", TestTargets.Create());
        await WaitUntilWaiting(sut);

        // Act
        var accepted = sut.QueueFromChat("passed along", Sender);
        var stillWaiting = sut.IsWaitingForUser;
        sut.Answer("c1", UserAnswer.Chose(["A"]));
        await turn.Bounded();

        // Assert
        accepted.Should().BeTrue();
        stillWaiting.Should().BeTrue();
        sut.Entries[1].ToolCalls.Single().Output.Should().Be("The user chose: \"A\".");
        sut.Entries.Where(e => e.Role == ChatRole.User).Select(e => e.Source).Should().Equal(null, Sender);
        sut.Entries[^1].Text.Should().Be("got it");
    }

    private static ToolSet HoldingTools(TaskCompletionSource started, TaskCompletionSource release)
    {
        var read = new FakeTool("read", isReadOnly: true, async (_, ct) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(TestTimeouts.Default, ct);
            return ToolResult.Ok("read done");
        });
        return new ToolSet([read], new ToolContext(Path.GetTempPath()), TimeProvider.System);
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

    private Chat Create(ScriptedChatClient client, ToolSet? tools = null)
    {
        return new Chat((_, _) => Task.FromResult<IChatClient>(client), _time, _log) { Tools = tools };
    }
}
