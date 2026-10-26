using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Providers;
using BlazeHarness.Core.Sessions;
using BlazeHarness.Core.Tests.Unit.Agent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable IDISP001 // The fixture disposes every service it creates.

namespace BlazeHarness.Core.Tests.Integration.Sessions;

public sealed class SessionServiceTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "bh-service-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly List<IDisposable> _disposables = [];

    public SessionServiceTests()
    {
        _paths = new AppPaths(_home, _home);
    }

    public void Dispose()
    {
        foreach (var d in _disposables)
        {
            d.Dispose();
        }

        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    [Fact]
    public async Task SendAsync_SavesTheConversationAndRemembersTheTarget()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("Hello")]);
        var sut = Create(client);
        var info = await sut.CreateAsync(cancellationToken: ct);

        // Act
        await sut.SendAsync(info.Id, "  What is\n the weather?  ", Target("p1", "fast"));

        // Assert
        var saved = sut.Store.Find(info.Id)!;
        saved.Title.Should().Be("What is the weather?");
        saved.TitleSource.Should().Be(TitleSource.Placeholder);
        saved.ProviderId.Should().Be("p1");
        saved.ModelId.Should().Be("fast");
        (await sut.Store.GetLog(info.Id).ReadAsync(ct)).Should().HaveCount(4);
    }

    [Fact]
    public async Task SendAsync_LongFirstMessage_GetsAShortPlaceholderTitle()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        var sut = Create(client);
        var info = await sut.CreateAsync(cancellationToken: ct);

        // Act
        await sut.SendAsync(info.Id, new string('a', 200), Target("p1", "fast"));

        // Assert
        sut.Store.Find(info.Id)!.Title.Should().HaveLength(49).And.EndWith("…");
    }

    [Fact]
    public async Task OpenAsync_AfterARestart_RestoresTheConversation()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("Hello")]);
        var first = Create(client);
        var info = await first.CreateAsync(cancellationToken: ct);
        await first.SendAsync(info.Id, "hi", Target("p1", "fast"));

        // Act
        var second = Create(client);
        var session = await second.OpenAsync(info.Id);

        // Assert
        session.Should().NotBeNull();
        session.Entries.Select(e => (e.Role, e.Text)).Should().Equal((ChatRole.User, "hi"), (ChatRole.Assistant, "Hello"));
    }

    [Fact]
    public async Task OpenAsync_UnknownSession_ReturnsNull()
    {
        // Arrange
        using var client = new ScriptedChatClient();
        var sut = Create(client);

        // Act
        var session = await sut.OpenAsync("missing");

        // Assert
        session.Should().BeNull();
    }

    [Fact]
    public async Task OpenAsync_Twice_ReturnsTheSameLiveSession()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient();
        var sut = Create(client);
        var info = await sut.CreateAsync(cancellationToken: ct);

        // Act
        var a = await sut.OpenAsync(info.Id);
        var b = await sut.OpenAsync(info.Id);

        // Assert
        b.Should().BeSameAs(a);
    }

    [Fact]
    public async Task OpenAsync_ReplyCutOffByARestart_IsInterruptedAndCanBeRetried()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient();
        var first = Create(client);
        var info = await first.CreateAsync(cancellationToken: ct);
        var user = Guid.NewGuid();
        var reply = Guid.NewGuid();
        await first.Store.GetLog(info.Id).AppendAsync(
        [
            new UserMessageEvent(DateTimeOffset.UnixEpoch, user, "hi"),
            new AssistantStartEvent(DateTimeOffset.UnixEpoch, reply, "P1 · fast"),
            new AssistantDeltaEvent(DateTimeOffset.UnixEpoch, reply, "half", string.Empty),
        ],
        ct);

        // Act
        var session = await Create(client).OpenAsync(info.Id);

        // Assert
        session.Should().NotBeNull();
        session.Entries[1].Status.Should().Be(EntryStatus.Interrupted);
        session.Entries[1].Text.Should().Be("half");
        session.CanRetry.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_WithNoPageAttached_FinishesAndIsSavedForLater()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new GatedChatClient();
        var sut = Create(client);
        var info = await sut.CreateAsync(cancellationToken: ct);

        // Act
        var turn = sut.SendAsync(info.Id, "hi", Target("p1", "fast"));
        await client.Started.WaitAsync(ct);
        client.Release();
        await turn;

        // Assert
        var reopened = await Create(client).OpenAsync(info.Id);
        reopened.Should().NotBeNull();
        reopened.Entries[1].Text.Should().Be("Hello");
        reopened.Entries[1].Status.Should().Be(EntryStatus.Done);
    }

    [Fact]
    public async Task OpenAsync_WhileAReplyStreams_AttachesAndFollowsTheLiveReply()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new GatedChatClient();
        var sut = Create(client);
        var info = await sut.CreateAsync(cancellationToken: ct);
        var turn = sut.SendAsync(info.Id, "hi", Target("p1", "fast"));
        await client.Started.WaitAsync(ct);

        // Act
        var secondTab = await sut.OpenAsync(info.Id);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        secondTab!.Changed += (_, _) =>
        {
            if (!secondTab.IsRunning)
            {
                finished.TrySetResult();
            }
        };
        var seenWhileRunning = (secondTab.IsRunning, secondTab.Entries[1].Text);
        client.Release();
        await finished.Task.WaitAsync(ct);
        await turn;

        // Assert
        seenWhileRunning.Should().Be((true, "Hel"));
        secondTab.Entries[1].Text.Should().Be("Hello");
        sut.IsRunning(info.Id).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_WhileRunning_RefusesAndKeepsTheSession()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new GatedChatClient();
        var sut = Create(client);
        var info = await sut.CreateAsync(cancellationToken: ct);
        var turn = sut.SendAsync(info.Id, "hi", Target("p1", "fast"));
        await client.Started.WaitAsync(ct);

        // Act
        var deleted = await sut.DeleteAsync(info.Id);
        client.Release();
        await turn;

        // Assert
        deleted.Should().BeFalse();
        sut.Store.Find(info.Id).Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteAsync_WhenIdle_RemovesTheSessionFromDisk()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        var sut = Create(client);
        var info = await sut.CreateAsync(cancellationToken: ct);
        await sut.SendAsync(info.Id, "hi", Target("p1", "fast"));

        // Act
        var deleted = await sut.DeleteAsync(info.Id);

        // Assert
        deleted.Should().BeTrue();
        (await sut.OpenAsync(info.Id)).Should().BeNull();
        Directory.GetDirectories(_paths.SessionsDirectory).Should().BeEmpty();
    }

    private static ChatTarget Target(string provider, string model)
    {
        var modelConfig = new ModelConfig { Id = model, ContextWindow = 8000, MaxOutputTokens = 1000 };
        var providerConfig = new ProviderConfig
        {
            Id = provider,
            DisplayName = provider.ToUpperInvariant(),
            BaseUrl = "http://localhost/v1",
            Models = [modelConfig],
        };
        return new ChatTarget(providerConfig, modelConfig, null);
    }

    private SessionService Create(IChatClient client)
    {
        var store = new SessionStore(_paths, NullLoggerFactory.Instance);
        var service = new SessionService(store, (_, _) => Task.FromResult(client), NullLoggerFactory.Instance);
        _disposables.Add(service);
        _disposables.Add(store);
        return service;
    }
}
