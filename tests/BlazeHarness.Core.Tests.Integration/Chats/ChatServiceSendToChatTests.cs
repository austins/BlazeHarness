using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Chats.Events;
using BlazeHarness.Core.Chats.Titles;
using BlazeHarness.Core.Config;
using BlazeHarness.Core.Providers;
using BlazeHarness.Core.Secrets;
using BlazeHarness.Core.Tools;
using BlazeHarness.Tests.Shared;
using BlazeHarness.Tests.Shared.Agent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using ChatInfo = BlazeHarness.Core.Chats.ChatInfo;
using ChatService = BlazeHarness.Core.Chats.ChatService;
using ChatStore = BlazeHarness.Core.Chats.ChatStore;

#pragma warning disable IDISP001 // The fixture disposes every service it creates.

namespace BlazeHarness.Core.Tests.Integration.Chats;

public sealed class ChatServiceSendToChatTests : IAsyncLifetime
{
    private readonly TempFolder _temp = new("bh-svc-send-");
    private readonly AppPaths _paths;
    private readonly JsonFileStore<AppConfig> _config;
    private readonly JsonFileStore<ProvidersDocument> _providersFile;
    private readonly FileSecretStore _secrets;
    private readonly ProviderService _providers;
    private readonly ChatStore _store;
    private readonly ScriptedChatClient _client = new([ScriptedChatClient.Text("done")]);
    private readonly ChatService _sut;
    private readonly List<ChatService> _extra = [];

    public ChatServiceSendToChatTests()
    {
        _paths = new AppPaths(Path.Combine(_temp.Root, "app"), _temp.Root);
        _config = new JsonFileStore<AppConfig>(Path.Combine(_temp.Root, "config.json"), NullLogger<JsonFileStore<AppConfig>>.Instance, TimeProvider.System, watch: false);
        _providersFile = new JsonFileStore<ProvidersDocument>(Path.Combine(_temp.Root, "providers.json"), NullLogger<JsonFileStore<ProvidersDocument>>.Instance, TimeProvider.System, watch: false);
        _secrets = new FileSecretStore(Path.Combine(_temp.Root, "secrets.json"));
        _providers = new ProviderService(_config, _providersFile, _secrets, new ApiKeyResolver(_secrets, _ => null));
        _store = new ChatStore(_paths, NullLoggerFactory.Instance, TimeProvider.System);
        _sut = new ChatService(_store, (_, _) => Task.FromResult<IChatClient>(_client), NullLoggerFactory.Instance, TimeProvider.System, providers: _providers);
    }

    public async ValueTask InitializeAsync()
    {
        await _providers.SaveAsync(
            new ProviderConfig
            {
                Id = "p",
                DisplayName = "p",
                BaseUrl = new Uri("https://api.example.com/v1"),
                Models = [new ModelConfig { Id = "m", ContextWindow = 8000, MaxOutputTokens = 1000 }],
            },
            null,
            TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        foreach (var service in _extra)
        {
            service.Dispose();
        }

        _sut.Dispose();
        _store.Dispose();
        _config.Dispose();
        _providersFile.Dispose();
        _secrets.Dispose();
        _client.Dispose();
        _temp.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ResolveSendToChat_ReferenceOrExactTitle_FindsTheChat()
    {
        // Arrange
        var (source, destination) = await TwoChatsAsync();

        // Act
        var byReference = _sut.ResolveSendToChat(source.Id, ChatReferenceOf(destination.Id), null, "go");
        var byTitle = _sut.ResolveSendToChat(source.Id, "story 2", null, "go");

        // Assert
        byReference.Plan.Should().Match<SendToChatPlan>(p => p.ChatId == destination.Id && p.SameScope);
        byTitle.Plan.Should().Match<SendToChatPlan>(p => p.ChatId == destination.Id);
    }

    [Fact]
    public async Task ResolveSendToChat_SameTitleTwice_ListsTheCandidates()
    {
        // Arrange
        var (source, _) = await TwoChatsAsync();
        var twin = await _sut.CreateAsync(cancellationToken: TestContext.Current.CancellationToken);
        await _store.RenameAsync(twin.Id, "Story 2", TestContext.Current.CancellationToken);

        // Act
        var result = _sut.ResolveSendToChat(source.Id, "Story 2", null, "go");

        // Assert
        result.Plan.Should().BeNull();
        result.Error.Should().Contain("2 chats").And.Contain("chat:" + twin.Id);
    }

    [Fact]
    public async Task ResolveSendToChat_OwnChatOrUnknownChat_IsRefused()
    {
        // Arrange
        var (source, _) = await TwoChatsAsync();

        // Act
        var self = _sut.ResolveSendToChat(source.Id, ChatReferenceOf(source.Id), null, "go");
        var unknown = _sut.ResolveSendToChat(source.Id, "chat:nope", null, "go");

        // Assert
        self.Plan.Should().BeNull();
        self.Error.Should().Contain("itself");
        unknown.Plan.Should().BeNull();
    }

    [Fact]
    public async Task ResolveSendToChat_ChatInAnotherFolder_IsNotTheSameScope()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var other = _temp.CreateDirectory("other");
        var source = await _sut.CreateAsync(_temp.CreateDirectory("work"), ct);
        var destination = await _sut.CreateAsync(other, ct);
        var general = await _sut.CreateAsync(cancellationToken: ct);

        // Act
        var toFolder = _sut.ResolveSendToChat(source.Id, ChatReferenceOf(destination.Id), null, "go");
        var toGeneral = _sut.ResolveSendToChat(source.Id, ChatReferenceOf(general.Id), null, "go");

        // Assert
        toFolder.Plan!.SameScope.Should().BeFalse();
        toGeneral.Plan!.SameScope.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveSendToChat_NewChat_UsesTheSourceFolderUnlessToldOtherwise()
    {
        // Arrange
        var work = _temp.CreateDirectory("work");
        var source = await _sut.CreateAsync(work, TestContext.Current.CancellationToken);

        // Act
        var inherited = _sut.ResolveSendToChat(source.Id, "new", null, "go");
        var none = _sut.ResolveSendToChat(source.Id, "NEW", "none", "go");
        var unknown = _sut.ResolveSendToChat(source.Id, "new", Path.Combine(_temp.Root, "unknown"), "go");

        // Assert
        inherited.Plan!.Creates.Should().BeTrue();
        inherited.Plan.FolderPath.Should().Be(source.FolderPath);
        inherited.Plan.SameScope.Should().BeTrue();
        none.Plan!.FolderPath.Should().BeNull();
        none.Plan.SameScope.Should().BeFalse();
        unknown.Plan.Should().BeNull();
    }

    [Fact]
    public async Task SendToChatAsync_IdleChat_StartsATurnWithTheAttributedMessage()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (source, destination) = await TwoChatsAsync();
        var plan = Plan(destination.Id);

        // Act
        var result = await _sut.SendToChatAsync(source.Id, (await _sut.OpenAsync(source.Id, ct))!, plan, ct);

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().Contain("A turn started there");
        var chat = (await _sut.OpenAsync(destination.Id, ct))!;
        await WaitForAsync(() => !chat.IsReplying && chat.Entries.Any(e => e.Role == ChatRole.Assistant));
        var user = chat.Entries.First(e => e.Role == ChatRole.User);
        user.Origin.Should().Be(UserMessageOrigin.FromChat);
        user.Source.Should().Be(new ChatSource(source.Id, source.Title, 1));
        user.Text.Should().Contain("Story 1").And.Contain("Build the parser.");
        _store.Find(destination.Id)!.ModelId.Should().Be("m");
    }

    [Fact]
    public async Task SendToChatAsync_NewChat_CreatesItAndStartsATurn()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var source = await _sut.CreateAsync(cancellationToken: ct);
        var before = _store.List().Count;
        var plan = new SendToChatPlan(null, null, null, SameScope: true, "Start here.");

        // Act
        var result = await _sut.SendToChatAsync(source.Id, (await _sut.OpenAsync(source.Id, ct))!, plan, ct);

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().StartWith("Created and sent to");
        _store.List().Should().HaveCount(before + 1);
    }

    [Fact]
    public async Task SendToChatAsync_ChatThatIsBusy_QueuesTheMessage()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var gated = new GatedChatClient();
        var service = CreateService(gated);
        var source = await service.CreateAsync(cancellationToken: ct);
        var destination = await service.CreateAsync(cancellationToken: ct);
        var running = service.SendAsync(destination.Id, "work", TestTargets.Create("p", "m"), ct);
        await gated.Started.Bounded();

        // Act
        var result = await service.SendToChatAsync(source.Id, (await service.OpenAsync(source.Id, ct))!, Plan(destination.Id), ct);
        gated.Release();
        await running.Bounded();

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().Contain("busy");
        var chat = (await service.OpenAsync(destination.Id, ct))!;
        await WaitForAsync(() => !chat.IsReplying && chat.Entries.Any(e => e.Origin == UserMessageOrigin.FromChat));
        chat.Entries.Count(e => e.Role == ChatRole.User).Should().Be(2);
    }

    [Fact]
    public async Task SendToChatAsync_MessageThatAlreadyPassedThroughThreeChats_IsRefused()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (source, destination) = await TwoChatsAsync();
        var chat = (await _sut.OpenAsync(source.Id, ct))!;
        await _sut.SendToChatAsync(source.Id, chat, Plan(destination.Id) with { Message = "one" }, ct);
        var destinationChat = (await _sut.OpenAsync(destination.Id, ct))!;
        await WaitForAsync(() => !destinationChat.IsReplying);

        // Act
        var next = await _sut.CreateAsync(cancellationToken: ct);
        var hop2 = await _sut.SendToChatAsync(destination.Id, destinationChat, Plan(next.Id), ct);
        var nextChat = (await _sut.OpenAsync(next.Id, ct))!;
        await WaitForAsync(() => !nextChat.IsReplying);
        var last = await _sut.CreateAsync(cancellationToken: ct);
        var hop3 = await _sut.SendToChatAsync(next.Id, nextChat, Plan(last.Id), ct);
        var lastChat = (await _sut.OpenAsync(last.Id, ct))!;
        await WaitForAsync(() => !lastChat.IsReplying);
        var another = await _sut.CreateAsync(cancellationToken: ct);
        var hop4 = await _sut.SendToChatAsync(last.Id, lastChat, Plan(another.Id), ct);

        // Assert
        hop2.IsError.Should().BeFalse();
        hop3.IsError.Should().BeFalse();
        hop4.IsError.Should().BeTrue();
        hop4.Output.Should().Contain("without the user");
    }

    [Fact]
    public async Task SendToChatAsync_ReplayedLog_KeepsTheSource()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (source, destination) = await TwoChatsAsync();
        await _sut.SendToChatAsync(source.Id, (await _sut.OpenAsync(source.Id, ct))!, Plan(destination.Id), ct);
        var chat = (await _sut.OpenAsync(destination.Id, ct))!;
        await WaitForAsync(() => !chat.IsReplying && chat.Entries.Any(e => e.Role == ChatRole.Assistant));

        // Act
        var events = await _store.GetLog(destination.Id).ReadAsync(ct);

        // Assert
        events.OfType<UserMessageEvent>().Should().ContainSingle().Which.Source.Should().Be(new ChatSource(source.Id, source.Title, 1));
    }

    [Fact]
    public async Task SendToChatAsync_NoModelConfigured_ReturnsAnErrorAndCreatesNoChat()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await _providers.RemoveAsync("p", ct);
        var source = await _sut.CreateAsync(cancellationToken: ct);
        var before = _store.List().Count;

        // Act
        var result = await _sut.SendToChatAsync(source.Id, (await _sut.OpenAsync(source.Id, ct))!, new SendToChatPlan(null, null, null, SameScope: true, "go"), ct);

        // Assert
        result.IsError.Should().BeTrue();
        _store.List().Should().HaveCount(before);
    }

    [Fact]
    public async Task SendToChatAsync_SenderWithANegativeHopCountInItsLog_CountsFromZero()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (source, destination) = await TwoChatsAsync();
        var crafted = new UserMessageEvent(DateTimeOffset.UnixEpoch, Guid.NewGuid(), "do it", null, UserMessageOrigin.FromChat, new ChatSource("other", "Boss", -1000));
        await _store.GetLog(source.Id).AppendAsync([crafted], ct);
        var service = CreateService(_client);
        var sourceChat = (await service.OpenAsync(source.Id, ct))!;

        // Act
        var result = await service.SendToChatAsync(source.Id, sourceChat, Plan(destination.Id), ct);

        // Assert
        result.IsError.Should().BeFalse();
        var chat = (await service.OpenAsync(destination.Id, ct))!;
        await WaitForAsync(() => !chat.IsReplying && chat.Entries.Any(e => e.Role == ChatRole.Assistant));
        chat.Entries.First(e => e.Role == ChatRole.User).Source!.Hops.Should().Be(1);
    }

    [Fact]
    public async Task SendToChatAsync_SenderWithoutATitle_IsNamedByItsReference()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (source, destination) = await TwoChatsAsync();
        await _store.RenameAsync(source.Id, "  ", ct);

        // Act
        await _sut.SendToChatAsync(source.Id, (await _sut.OpenAsync(source.Id, ct))!, Plan(destination.Id), ct);

        // Assert
        var chat = (await _sut.OpenAsync(destination.Id, ct))!;
        await WaitForAsync(() => !chat.IsReplying && chat.Entries.Any(e => e.Role == ChatRole.Assistant));
        var user = chat.Entries.First(e => e.Role == ChatRole.User);
        user.Source!.Title.Should().Be("chat:" + source.Id);
        user.Text.Should().StartWith($"Message from the chat \"chat:{source.Id}\"");
    }

    [Fact]
    public async Task SendToChatAsync_NewChat_TitlesItFromTheMessageNotTheAttribution()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var asked = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var titles = Substitute.For<ITitleGenerator>();
        titles.GenerateAsync(Arg.Any<ChatTarget>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                asked.TrySetResult(call.ArgAt<string>(1));
                return Task.FromResult<string?>("Parser work");
            });
        var service = CreateService(_client, titles);
        var source = await service.CreateAsync(cancellationToken: ct);

        // Act
        var result = await service.SendToChatAsync(source.Id, (await service.OpenAsync(source.Id, ct))!, new SendToChatPlan(null, null, null, SameScope: true, "Build the parser."), ct);

        // Assert
        result.IsError.Should().BeFalse();
        (await asked.Task.Bounded()).Should().Be("Build the parser.");
        var created = service.Store.List().Single(c => c.Id != source.Id);
        await WaitForAsync(() => service.Store.Find(created.Id)?.Title == "Parser work");
    }

    [Fact]
    public async Task SendToChatAsync_AlreadyCancelled_CreatesNoChat()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var source = await _sut.CreateAsync(cancellationToken: ct);
        var sourceChat = (await _sut.OpenAsync(source.Id, ct))!;
        var before = _store.List().Count;
        var cancelled = new CancellationToken(canceled: true);

        // Act
        var act = () => _sut.SendToChatAsync(source.Id, sourceChat, new SendToChatPlan(null, null, null, SameScope: true, "go"), cancelled);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        _store.List().Should().HaveCount(before);
    }

    [Fact]
    public async Task SendToChatAsync_ChatThatIsBusyAndThenStopped_KeepsTheMessageForTheUserToRetry()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var gated = new GatedChatClient();
        var service = CreateService(gated);
        var source = await service.CreateAsync(cancellationToken: ct);
        var destination = await service.CreateAsync(cancellationToken: ct);
        var running = service.SendAsync(destination.Id, "work", TestTargets.Create("p", "m"), ct);
        await gated.Started.Bounded();
        await service.SendToChatAsync(source.Id, (await service.OpenAsync(source.Id, ct))!, Plan(destination.Id), ct);
        var chat = (await service.OpenAsync(destination.Id, ct))!;

        // Act
        chat.Stop();
        await running.Bounded();
        await WaitForAsync(() => !chat.IsReplying);

        // Assert
        chat.TakeQueued().Should().BeEmpty();
        chat.Entries[^1].Origin.Should().Be(UserMessageOrigin.FromChat);
        chat.CanRetry.Should().BeTrue();
        (await _store.GetLog(destination.Id).ReadAsync(ct)).OfType<UserMessageEvent>().Last().Source.Should().NotBeNull();
        gated.Release();
    }

    private ChatService CreateService(IChatClient client, ITitleGenerator? titles = null)
    {
        var service = new ChatService(_store, (_, _) => Task.FromResult(client), NullLoggerFactory.Instance, TimeProvider.System, titles: titles, providers: _providers);
        _extra.Add(service);
        return service;
    }

    private static string ChatReferenceOf(string id) => "chat:" + id;

    private static SendToChatPlan Plan(string destinationId) => new(destinationId, "Story 2", null, SameScope: true, "Build the parser.");

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TestTimeouts.Default);
        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    private async Task<(ChatInfo Source, ChatInfo Destination)> TwoChatsAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = await _sut.CreateAsync(cancellationToken: ct);
        await _store.RenameAsync(source.Id, "Story 1", ct);
        var destination = await _sut.CreateAsync(cancellationToken: ct);
        await _store.RenameAsync(destination.Id, "Story 2", ct);
        return (_store.Find(source.Id)!, _store.Find(destination.Id)!);
    }
}
