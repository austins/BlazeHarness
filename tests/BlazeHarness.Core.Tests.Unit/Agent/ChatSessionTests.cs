using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Folders;
using BlazeHarness.Core.Providers;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Unit.Agent;

public sealed class ChatSessionTests
{
    private static readonly ChatTarget Fast = Target("p1", "fast", null);

    [Fact]
    public async Task SendAsync_StreamsReply_AddsEntriesAndHistory()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("Hel"), ScriptedChatClient.Text("lo")]);
        using var sut = CreateSession(client);

        // Act
        await sut.SendAsync("hi", Fast);

        // Assert
        sut.Entries.Select(e => (e.Role, e.Text, e.Status)).Should().Equal(
            (ChatRole.User, "hi", EntryStatus.Done),
            (ChatRole.Assistant, "Hello", EntryStatus.Done));
        sut.History.Select(m => m.Text).Should().Equal("hi", "Hello");
        sut.IsRunning.Should().BeFalse();
        sut.Entries[1].Label.Should().Be("P1 · fast");
    }

    [Fact]
    public async Task SendAsync_BlankText_DoesNothing()
    {
        // Arrange
        using var client = new ScriptedChatClient();
        using var sut = CreateSession(client);

        // Act
        await sut.SendAsync("   ", Fast);

        // Assert
        sut.Entries.Should().BeEmpty();
        client.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_ClientFactoryThrows_MarksReplyFailed()
    {
        // Arrange
        using var sut = new ChatSession((_, _) => throw new InvalidOperationException("no key"));

        // Act
        await sut.SendAsync("hi", Fast);

        // Assert
        sut.Entries[1].Status.Should().Be(EntryStatus.Failed);
        sut.Entries[1].Error.Should().Be("no key");
        sut.CanRetry.Should().BeTrue();
        sut.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_ClientFactoryThrows_LogsAWarningWithTheReason()
    {
        // Arrange
        var logger = new LoggerFake<ChatSession>();
        using var sut = new ChatSession((_, _) => throw new InvalidOperationException("no key"), logger: logger);

        // Act
        await sut.SendAsync("hi", Fast);

        // Assert
        logger.Entries.Should().ContainSingle().Which.Message.Should().Be("A reply failed: no key");
        logger.Entries[0].LogLevel.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public async Task SendAsync_Stopped_KeepsPartialReplyAsCancelled()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("so far")]);
        using var session = CreateSession(client);
        session.Changed += (_, _) =>
        {
            if (session.Entries is [_, { Text: "so far" }])
            {
                session.Stop();
            }
        };

        // Act
        await session.SendAsync("hi", Fast);

        // Assert
        session.Entries[1].Status.Should().Be(EntryStatus.Cancelled);
        session.Entries[1].Text.Should().Be("so far");
        session.CanRetry.Should().BeTrue();
    }

    [Fact]
    public async Task RetryAsync_AfterFailure_DropsPartialReplyAndUsesNewTarget()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("par")], new InvalidOperationException("boom"))
            .Enqueue([ScriptedChatClient.Text("full answer")]);
        var targets = new List<string>();
        using var sut = CreateSession(client, t => targets.Add(t.Model.Id));
        await sut.SendAsync("hi", Fast);

        // Act
        await sut.RetryAsync(Target("p2", "smart", ThinkingLevel.High));

        // Assert
        sut.Entries.Select(e => e.Text).Should().Equal("hi", "full answer");
        sut.Entries[1].Status.Should().Be(EntryStatus.Done);
        sut.Entries[1].Label.Should().Be("P2 · smart · high");
        sut.History.Select(m => m.Text).Should().Equal("hi", "full answer");
        client.Requests[1].Select(m => m.Text).Should().Equal("hi");
        targets.Should().Equal("fast", "smart");
    }

    [Fact]
    public async Task SendAsync_WithProjectContext_SendsItAsASystemMessageAndKeepsItOutOfHistory()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        using var sut = CreateSession(client);
        sut.Project = new ProjectContext("/work/app", [new ProjectFile("/work/app/AGENTS.md", "Use tabs.", false)]);

        // Act
        await sut.SendAsync("hi", Fast);

        // Assert
        client.Requests.Should().ContainSingle();
        client.Requests[0].Select(m => m.Role).Should().Equal(ChatRole.System, ChatRole.User);
        client.Requests[0][0].Text.Should().Contain("Use tabs.").And.Contain("/work/app/AGENTS.md");
        sut.History.Select(m => m.Role).Should().Equal(ChatRole.User, ChatRole.Assistant);
    }

    [Fact]
    public async Task SendAsync_WithoutProjectContext_SendsNoSystemMessage()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        using var sut = CreateSession(client);

        // Act
        await sut.SendAsync("hi", Fast);

        // Assert
        client.Requests[0].Select(m => m.Role).Should().Equal(ChatRole.User);
    }

    [Fact]
    public async Task RetryAsync_AfterSuccess_DoesNothing()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        using var sut = CreateSession(client);
        await sut.SendAsync("hi", Fast);

        // Act
        await sut.RetryAsync(Fast);

        // Assert
        sut.Entries.Should().HaveCount(2);
        client.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task EditAsync_MiddleMessage_DropsItAndEverythingAfterAndAsksAgain()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("one")])
            .Enqueue([ScriptedChatClient.Text("two")])
            .Enqueue([ScriptedChatClient.Text("three")])
            .Enqueue([ScriptedChatClient.Text("new two")]);
        using var sut = CreateSession(client);
        await sut.SendAsync("a", Fast);
        await sut.SendAsync("b", Fast);
        await sut.SendAsync("c", Fast);
        var second = sut.Entries[2].Id;

        // Act
        await sut.EditAsync(second, "b2", Target("p2", "smart", null));

        // Assert
        sut.Entries.Select(e => e.Text).Should().Equal("a", "one", "b2", "new two");
        sut.History.Select(m => m.Text).Should().Equal("a", "one", "b2", "new two");
        client.Requests[3].Select(m => m.Text).Should().Equal("a", "one", "b2");
        sut.Entries[3].Label.Should().Be("P2 · smart");
    }

    [Fact]
    public async Task EditAsync_FirstMessage_RestartsTheConversation()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("one")])
            .Enqueue([ScriptedChatClient.Text("two")])
            .Enqueue([ScriptedChatClient.Text("fresh")]);
        using var sut = CreateSession(client);
        await sut.SendAsync("a", Fast);
        await sut.SendAsync("b", Fast);

        // Act
        await sut.EditAsync(sut.Entries[0].Id, "a2", Fast);

        // Assert
        sut.Entries.Select(e => e.Text).Should().Equal("a2", "fresh");
        client.Requests[2].Select(m => m.Text).Should().Equal("a2");
    }

    [Fact]
    public async Task EditAsync_AfterAFailedReplyWithNoText_KeepsHistoryAligned()
    {
        // Arrange
        using var client = new ScriptedChatClient([], new InvalidOperationException("boom"))
            .Enqueue([ScriptedChatClient.Text("one")])
            .Enqueue([ScriptedChatClient.Text("fixed")]);
        using var sut = CreateSession(client);
        await sut.SendAsync("a", Fast);
        await sut.SendAsync("b", Fast);

        // Act
        await sut.EditAsync(sut.Entries[2].Id, "b2", Fast);

        // Assert
        sut.History.Select(m => m.Text).Should().Equal("a", "b2", "fixed");
    }

    [Fact]
    public async Task EditAsync_AssistantMessageOrUnknownId_DoesNothing()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("one")]);
        using var sut = CreateSession(client);
        await sut.SendAsync("a", Fast);

        // Act
        await sut.EditAsync(sut.Entries[1].Id, "x", Fast);
        await sut.EditAsync(Guid.NewGuid(), "x", Fast);

        // Assert
        sut.Entries.Select(e => e.Text).Should().Equal("a", "one");
        client.Requests.Should().ContainSingle();
        sut.CanEdit(sut.Entries[0].Id).Should().BeTrue();
        sut.CanEdit(sut.Entries[1].Id).Should().BeFalse();
        sut.CanEdit(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public async Task EditAsync_BlankText_DoesNothing()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("one")]);
        using var sut = CreateSession(client);
        await sut.SendAsync("a", Fast);

        // Act
        await sut.EditAsync(sut.Entries[0].Id, "  ", Fast);

        // Assert
        sut.Entries.Select(e => e.Text).Should().Equal("a", "one");
    }

    [Fact]
    public async Task SendAsync_ModelChangedBetweenMessages_KeepsHistoryAndLabelsEachReply()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("one")])
            .Enqueue([ScriptedChatClient.Text("two")]);
        using var sut = CreateSession(client);

        // Act
        await sut.SendAsync("a", Fast);
        await sut.SendAsync("b", Target("p2", "smart", ThinkingLevel.Low));

        // Assert
        client.Requests[1].Select(m => m.Text).Should().Equal("a", "one", "b");
        sut.Entries.Where(e => e.Role == ChatRole.Assistant).Select(e => e.Label)
            .Should().Equal("P1 · fast", "P2 · smart · low");
    }

    private static ChatSession CreateSession(ScriptedChatClient client, Action<ChatTarget>? onTarget = null) =>
        new((target, _) =>
        {
            onTarget?.Invoke(target);
            return Task.FromResult<IChatClient>(client);
        });

    private static ChatTarget Target(string provider, string model, ThinkingLevel? level)
    {
        var modelConfig = new ModelConfig { Id = model, ContextWindow = 8000, MaxOutputTokens = 1000 };
        var providerConfig = new ProviderConfig
        {
            Id = provider,
            DisplayName = provider.ToUpperInvariant(),
            BaseUrl = "http://localhost/v1",
            Models = [modelConfig],
        };
        return new ChatTarget(providerConfig, modelConfig, level);
    }
}
