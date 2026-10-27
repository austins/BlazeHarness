using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Attachments;
using BlazeHarness.Core.Config;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Sessions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable IDISP001 // The fixture disposes every service it creates.

namespace BlazeHarness.Core.Tests.Integration.Sessions;

public sealed class SessionServiceTests : IDisposable
{
    private readonly TempFolder _temp = new("bh-service-");
    private readonly AppPaths _paths;
    private readonly List<IDisposable> _disposables = [];

    public SessionServiceTests()
    {
        _paths = new AppPaths(Path.Combine(_temp.Root, "app"), _temp.Root);
    }

    public void Dispose()
    {
        foreach (var d in _disposables)
        {
            d.Dispose();
        }

        _temp.Dispose();
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
        await sut.SendAsync(info.Id, "  What is\n the weather?  ", TestTargets.Create("p1", "fast"));

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
        await sut.SendAsync(info.Id, new string('a', 200), TestTargets.Create("p1", "fast"));

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
        await first.SendAsync(info.Id, "hi", TestTargets.Create("p1", "fast"));

        // Act
        var second = Create(client);
        var session = await second.OpenAsync(info.Id);

        // Assert
        session.Should().NotBeNull();
        session.Entries.Select(e => (e.Role, e.Text)).Should().Equal((ChatRole.User, "hi"), (ChatRole.Assistant, "Hello"));
    }

    [Fact]
    public async Task SetPermissionsAsync_ChangedSettings_AreSavedLoggedAndRestored()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient();
        var first = Create(client);
        var info = await first.CreateAsync(cancellationToken: ct);
        var settings = new PermissionSettings(AccessLevel.ReadOnly, ApprovalMode.AutoApprove);

        // Act
        await first.SetPermissionsAsync(info.Id, settings);
        var second = Create(client);
        var session = await second.OpenAsync(info.Id);

        // Assert
        first.Store.Find(info.Id)!.Permissions.Should().Be(settings);
        session!.Permissions.Settings.Should().Be(settings);
        (await first.Store.GetLog(info.Id).ReadAsync(ct)).Should().ContainSingle()
            .Which.Should().BeOfType<PermissionsChangedEvent>().Which.Access.Should().Be(AccessLevel.ReadOnly);
    }

    [Fact]
    public async Task SetPermissionsAsync_BackToDefaults_ClearsTheSavedChoice()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient();
        var sut = Create(client);
        var info = await sut.CreateAsync(cancellationToken: ct);
        await sut.SetPermissionsAsync(info.Id, new PermissionSettings(AccessLevel.Unrestricted, ApprovalMode.NeverAsk));

        // Act
        await sut.SetPermissionsAsync(info.Id, PermissionSettings.Default);

        // Assert
        var saved = sut.Store.Find(info.Id)!;
        saved.Access.Should().BeNull();
        saved.Approvals.Should().BeNull();
    }

    [Fact]
    public async Task OpenAsync_NewSession_ProtectsTheAppFolderExceptItsOwn()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient();
        var sut = Create(client);
        var info = await sut.CreateAsync(cancellationToken: ct);

        // Act
        var session = await sut.OpenAsync(info.Id);

        // Assert
        session!.Permissions.Sensitive().IsProtected(_paths.ConfigFile).Should().BeTrue();
        session.Permissions.Sensitive().IsProtected(Path.Combine(_paths.SessionDirectory(info.Id), "outputs", "a.txt")).Should().BeFalse();
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
        var turn = sut.SendAsync(info.Id, "hi", TestTargets.Create("p1", "fast"));
        await client.Started.WaitAsync(TestTimeouts.Default, ct);
        client.Release();
        await turn.Bounded();

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
        var turn = sut.SendAsync(info.Id, "hi", TestTargets.Create("p1", "fast"));
        await client.Started.WaitAsync(TestTimeouts.Default, ct);

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
        await finished.Task.WaitAsync(TestTimeouts.Default, ct);
        await turn.Bounded();

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
        var turn = sut.SendAsync(info.Id, "hi", TestTargets.Create("p1", "fast"));
        await client.Started.WaitAsync(TestTimeouts.Default, ct);

        // Act
        var deleted = await sut.DeleteAsync(info.Id);
        client.Release();
        await turn.Bounded();

        // Assert
        deleted.Should().BeFalse();
        sut.Store.Find(info.Id).Should().NotBeNull();
    }

    [Fact]
    public async Task EditAsync_FirstMessage_UpdatesAPlaceholderTitleAndSurvivesARestart()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("one")]).Enqueue([ScriptedChatClient.Text("two")]);
        var sut = Create(client);
        var info = await sut.CreateAsync(cancellationToken: ct);
        await sut.SendAsync(info.Id, "first draft", TestTargets.Create("p1", "fast"));
        var entryId = (await sut.OpenAsync(info.Id))!.Entries[0].Id;

        // Act
        await sut.EditAsync(info.Id, entryId, "better question", TestTargets.Create("p1", "fast"));

        // Assert
        sut.Store.Find(info.Id)!.Title.Should().Be("better question");
        var reopened = await Create(client).OpenAsync(info.Id);
        reopened!.Entries.Select(e => e.Text).Should().Equal("better question", "two");
    }

    [Fact]
    public async Task EditAsync_LaterMessage_KeepsTheTitle()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("one")])
            .Enqueue([ScriptedChatClient.Text("two")])
            .Enqueue([ScriptedChatClient.Text("three")]);
        var sut = Create(client);
        var info = await sut.CreateAsync(cancellationToken: ct);
        await sut.SendAsync(info.Id, "topic", TestTargets.Create("p1", "fast"));
        await sut.SendAsync(info.Id, "follow up", TestTargets.Create("p1", "fast"));
        var entryId = (await sut.OpenAsync(info.Id))!.Entries[2].Id;

        // Act
        await sut.EditAsync(info.Id, entryId, "other follow up", TestTargets.Create("p1", "fast"));

        // Assert
        sut.Store.Find(info.Id)!.Title.Should().Be("topic");
    }

    [Fact]
    public async Task EditAsync_WhileRunning_DoesNothing()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new GatedChatClient();
        var sut = Create(client);
        var info = await sut.CreateAsync(cancellationToken: ct);
        var turn = sut.SendAsync(info.Id, "hi", TestTargets.Create("p1", "fast"));
        await client.Started.WaitAsync(TestTimeouts.Default, ct);
        var session = (await sut.OpenAsync(info.Id))!;

        // Act
        await sut.EditAsync(info.Id, session.Entries[0].Id, "changed", TestTargets.Create("p1", "fast"));
        client.Release();
        await turn.Bounded();

        // Assert
        session.Entries[0].Text.Should().Be("hi");
    }

    [Fact]
    public async Task DeleteAsync_WhenIdle_RemovesTheSessionFromDisk()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        var sut = Create(client);
        var info = await sut.CreateAsync(cancellationToken: ct);
        await sut.SendAsync(info.Id, "hi", TestTargets.Create("p1", "fast"));

        // Act
        var deleted = await sut.DeleteAsync(info.Id);

        // Assert
        deleted.Should().BeTrue();
        (await sut.OpenAsync(info.Id)).Should().BeNull();
        Directory.GetDirectories(_paths.SessionsDirectory).Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_OnASessionThatWasDeleted_DoesNotStartAReply()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        var sut = Create(client);
        var info = await sut.CreateAsync(cancellationToken: ct);
        var session = (await sut.OpenAsync(info.Id))!;
        await sut.DeleteAsync(info.Id);

        // Act
        await session.SendAsync("late", [], TestTargets.Create("p1", "fast"));

        // Assert
        client.Requests.Should().BeEmpty();
        session.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ActivityChanged_ReplyStartsAndEnds_IsRaisedForBoth()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new GatedChatClient();
        var sut = Create(client);
        var info = await sut.CreateAsync(cancellationToken: ct);
        var seen = new List<bool>();
        sut.ActivityChanged += (_, _) =>
        {
            lock (seen)
            {
                seen.Add(sut.IsRunning(info.Id));
            }
        };

        // Act
        var turn = sut.SendAsync(info.Id, "hi", TestTargets.Create("p1", "fast"));
        await client.Started.WaitAsync(TestTimeouts.Default, ct);
        client.Release();
        await turn.Bounded();

        // Assert
        lock (seen)
        {
            seen.Should().Contain(true);
        }

        sut.IsRunning(info.Id).Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_WithATitleGenerator_ReplacesThePlaceholderAfterTheFirstExchange()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("Hello")]);
        var titles = Substitute.For<ITitleGenerator>();
        titles.GenerateAsync(Arg.Any<ChatTarget>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("Greeting");
        var sut = Create(client, titles);
        var info = await sut.CreateAsync(cancellationToken: ct);

        // Act
        await sut.SendAsync(info.Id, "hi there", TestTargets.Create("p1", "fast"));
        await sut.PendingTitleAsync(info.Id);

        // Assert
        var saved = sut.Store.Find(info.Id)!;
        saved.Title.Should().Be("Greeting");
        saved.TitleSource.Should().Be(TitleSource.Generated);
        await titles.Received(1).GenerateAsync(Arg.Any<ChatTarget>(), "hi there", "Hello", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_AfterAToolChainWhoseFirstResponseHasNoText_TitlesFromTheFirstTextReply()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(_temp.Root, "work");
        Directory.CreateDirectory(folder);
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "list_files")])
            .Enqueue([ScriptedChatClient.Text("The folder is empty.")]);
        var titles = Substitute.For<ITitleGenerator>();
        titles.GenerateAsync(Arg.Any<ChatTarget>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("Empty folder");
        var sut = Create(client, titles);
        var info = await sut.CreateAsync(folder, ct);

        // Act
        await sut.SendAsync(info.Id, "what is here?", TestTargets.Create("p1", "fast"));
        await sut.PendingTitleAsync(info.Id);

        // Assert
        sut.Store.Find(info.Id)!.Title.Should().Be("Empty folder");
        await titles.Received(1).GenerateAsync(Arg.Any<ChatTarget>(), "what is here?", "The folder is empty.", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_AfterAGeneratedTitle_DoesNotAskAgain()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("Hello")]).Enqueue([ScriptedChatClient.Text("Again")]);
        var titles = Substitute.For<ITitleGenerator>();
        titles.GenerateAsync(Arg.Any<ChatTarget>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("Greeting");
        var sut = Create(client, titles);
        var info = await sut.CreateAsync(cancellationToken: ct);
        await sut.SendAsync(info.Id, "hi", TestTargets.Create("p1", "fast"));
        await sut.PendingTitleAsync(info.Id);

        // Act
        await sut.SendAsync(info.Id, "more", TestTargets.Create("p1", "fast"));
        await sut.PendingTitleAsync(info.Id);

        // Assert
        await titles.Received(1).GenerateAsync(Arg.Any<ChatTarget>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_WhenTheTitleRequestFails_KeepsThePlaceholder()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("Hello")]);
        var titles = Substitute.For<ITitleGenerator>();
        titles.GenerateAsync(Arg.Any<ChatTarget>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string?>(_ => throw new InvalidOperationException("boom"));
        var sut = Create(client, titles);
        var info = await sut.CreateAsync(cancellationToken: ct);

        // Act
        await sut.SendAsync(info.Id, "hi", TestTargets.Create("p1", "fast"));
        await sut.PendingTitleAsync(info.Id);

        // Assert
        var saved = sut.Store.Find(info.Id)!;
        saved.Title.Should().Be("hi");
        saved.TitleSource.Should().Be(TitleSource.Placeholder);
    }

    [Fact]
    public async Task SendAsync_AfterThreeFailedTitleRequests_StopsAskingForATitle()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient([.. Enumerable.Repeat(0, 5).Select(_ => ScriptedChatClient.Text("Hello"))]);
        var titles = Substitute.For<ITitleGenerator>();
        titles.GenerateAsync(Arg.Any<ChatTarget>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string?>(_ => throw new InvalidOperationException("boom"));
        var sut = Create(client, titles);
        var info = await sut.CreateAsync(cancellationToken: ct);
        var target = TestTargets.Create("p1", "fast");

        // Act
        for (var i = 0; i < 5; i++)
        {
            await sut.SendAsync(info.Id, "hi " + i, target);
            await sut.PendingTitleAsync(info.Id);
        }

        // Assert
        await titles.Received(3).GenerateAsync(Arg.Any<ChatTarget>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_WhenTheUserRenamesWhileTitling_KeepsTheUsersTitle()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("Hello")]);
        var release = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var titles = Substitute.For<ITitleGenerator>();
#pragma warning disable VSTHRD003, IDISP013 // The source is completed by this test, and the client outlives the call.
        titles.GenerateAsync(Arg.Any<ChatTarget>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => release.Task);
#pragma warning restore VSTHRD003, IDISP013
        var sut = Create(client, titles);
        var info = await sut.CreateAsync(cancellationToken: ct);
        await sut.SendAsync(info.Id, "hi", TestTargets.Create("p1", "fast"));

        // Act
        await sut.Store.RenameAsync(info.Id, "Mine", ct);
        release.SetResult("Generated");
        await sut.PendingTitleAsync(info.Id);

        // Assert
        var saved = sut.Store.Find(info.Id)!;
        saved.Title.Should().Be("Mine");
        saved.TitleSource.Should().Be(TitleSource.User);
    }

    [Fact]
    public async Task SendAsync_WhenTheReplyFails_DoesNotAskForATitle()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("Hel")], failure: new InvalidOperationException("down"));
        var titles = Substitute.For<ITitleGenerator>();
        var sut = Create(client, titles);
        var info = await sut.CreateAsync(cancellationToken: ct);

        // Act
        await sut.SendAsync(info.Id, "hi", TestTargets.Create("p1", "fast"));
        await sut.PendingTitleAsync(info.Id);

        // Assert
        await titles.DidNotReceiveWithAnyArgs().GenerateAsync(default!, default!, default!, ct);
    }

    [Fact]
    public async Task SendAsync_SessionInAFolder_LetsTheModelReadFilesThere()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(_temp.Root, "work");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "notes.txt"), "secret plans", ct);
        using var client = new ScriptedChatClient([ScriptedChatClient.Call("c1", "read_file", """{"path":"notes.txt"}""")])
            .Enqueue([ScriptedChatClient.Text("It says secret plans.")]);
        var sut = Create(client);
        var info = await sut.CreateAsync(folder, ct);

        // Act
        await sut.SendAsync(info.Id, "read notes", TestTargets.Create("p1", "fast"));

        // Assert
        var session = (await sut.OpenAsync(info.Id))!;
        session.Entries[1].ToolCalls.Single().Output.Should().Contain("secret plans");
        client.Options[0]!.Tools.Should().Contain(t => t.Name == "write_file");
        client.Requests[0][0].Role.Should().Be(ChatRole.System);
        client.Requests[0][0].Text.Should().Contain("BlazeHarness");
    }

    [Fact]
    public async Task SendAsync_SessionWithoutAFolder_WorksInItsOutputsFolder()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("Hello")]);
        var sut = Create(client);
        var info = await sut.CreateAsync(cancellationToken: ct);

        // Act
        await sut.SendAsync(info.Id, "hi", TestTargets.Create("p1", "fast"));

        // Assert
        client.Options[0]!.Tools.Should().Contain(t => t.Name == "write_file").And.Contain(t => t.Name == "read_attachment");
        var outputs = sut.Store.OutputsDirectory(info.Id);
        Directory.Exists(outputs).Should().BeTrue();
        client.Requests[0][0].Role.Should().Be(ChatRole.System);
        client.Requests[0][0].Text.Should().Contain(Path.Combine("sessions", info.Id, "outputs"));
    }

    [Fact]
    public async Task CreateAsync_WithGlobalDefaults_StartsWithThem()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient([]);
        var defaults = await NewDefaultsAsync(new PermissionSettings(AccessLevel.ReadOnly, ApprovalMode.AutoApprove), allowBypass: true);
        var sut = Create(client, defaults: defaults);

        // Act
        var info = await sut.CreateAsync(_temp.Root, ct);
        var session = await sut.OpenAsync(info.Id);

        // Assert
        info.Permissions.Should().Be(new PermissionSettings(AccessLevel.ReadOnly, ApprovalMode.AutoApprove));
        session!.Permissions.Settings.Should().Be(info.Permissions);
    }

    [Fact]
    public async Task OpenAsync_BypassTurnedOff_RunsARiskyChatAsTheSaferOptions()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient([]);
        var defaults = await NewDefaultsAsync(PermissionSettings.Default, allowBypass: true);
        var sut = Create(client, defaults: defaults);
        var info = await sut.CreateAsync(_temp.Root, ct);
        await sut.SetPermissionsAsync(info.Id, new PermissionSettings(AccessLevel.Unrestricted, ApprovalMode.NeverAsk));
        var session = await sut.OpenAsync(info.Id);

        // Act
        await defaults.SaveAsync(PermissionSettings.Default, allowBypass: false, ct);

        // Assert
        session!.Permissions.Settings.Should().Be(new PermissionSettings(AccessLevel.WriteInFolder, ApprovalMode.AutoApprove));
        sut.Store.Find(info.Id)!.Permissions.Should().Be(new PermissionSettings(AccessLevel.Unrestricted, ApprovalMode.NeverAsk));
    }

    [Fact]
    public async Task CreateAsync_WithoutAFolder_StartsReadOnly()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient([]);
        var sut = Create(client);

        // Act
        var info = await sut.CreateAsync(cancellationToken: ct);
        var session = await sut.OpenAsync(info.Id);

        // Assert
        info.Permissions.Access.Should().Be(AccessLevel.ReadOnly);
        session!.Permissions.Settings.Access.Should().Be(AccessLevel.ReadOnly);
        sut.Store.Find(info.Id)!.Permissions.Approvals.Should().Be(ApprovalMode.AskFirst);
    }

    [Fact]
    public async Task SendAsync_WithAnAttachedFile_PutsTheFileInTheMessageAndServesItAfterwards()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("Got it")]);
        var sut = Create(client);
        var info = await sut.CreateAsync(cancellationToken: ct);
        await using var content = new MemoryStream("some notes"u8.ToArray());
        var file = await sut.SaveAttachmentAsync(info.Id, "notes.txt", content, ct);

        // Act
        await sut.SendAsync(info.Id, "summarize", [file!], TestTargets.Create("p1", "fast"));

        // Assert
        client.Requests[0].Should().Contain(m => m.Role == ChatRole.User && m.Text.Contains("summarize") && m.Text.Contains("some notes"));
        sut.FindAttachment(info.Id, file!.Id).Should().Match<AttachmentFile>(f => f.Name == "notes.txt");
        sut.Store.Find(info.Id)!.Title.Should().Be("summarize");
    }

    [Fact]
    public async Task SendAsync_AttachedFileWithoutText_TitlesTheSessionWithTheFileName()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("Got it")]);
        var sut = Create(client);
        var info = await sut.CreateAsync(cancellationToken: ct);
        await using var content = new MemoryStream("some notes"u8.ToArray());
        var file = await sut.SaveAttachmentAsync(info.Id, "notes.txt", content, ct);

        // Act
        await sut.SendAsync(info.Id, string.Empty, [file!], TestTargets.Create("p1", "fast"));

        // Assert
        sut.Store.Find(info.Id)!.Title.Should().Be("notes.txt");
    }

    [Fact]
    public async Task SaveAttachmentAsync_UnknownSession_ReturnsNull()
    {
        // Arrange
        await using var content = new MemoryStream("x"u8.ToArray());
        using var client = new ScriptedChatClient([]);
        var sut = Create(client);

        // Act
        var file = await sut.SaveAttachmentAsync("nope", "a.txt", content, TestContext.Current.CancellationToken);

        // Assert
        file.Should().BeNull();
        sut.FindAttachment("nope", "0123abcd").Should().BeNull();
    }

    [Fact]
    public async Task FindFolderImage_ImageInsideTheSessionFolder_IsFoundAndAnythingElseIsNot()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(_temp.Root, "work");
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, "a.png"), [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1], ct);
        await File.WriteAllTextAsync(Path.Combine(folder, "a.txt"), "text", ct);
        await File.WriteAllBytesAsync(Path.Combine(_temp.Root, "outside.png"), [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1], ct);
        using var client = new ScriptedChatClient([]);
        var sut = Create(client);
        var info = await sut.CreateAsync(folder, ct);
        var withoutFolder = await sut.CreateAsync(cancellationToken: ct);

        // Act
        var found = sut.FindFolderImage(info.Id, "a.png");

        // Assert
        found.Should().Match<AttachmentFile>(f => f.MediaType == "image/png");
        sut.FindFolderImage(info.Id, "a.txt").Should().BeNull();
        sut.FindFolderImage(info.Id, "../outside.png").Should().BeNull();
        sut.FindFolderImage(withoutFolder.Id, "a.png").Should().BeNull();
        sut.FindFolderImage("nope", "a.png").Should().BeNull();
    }

    private async Task<PermissionDefaults> NewDefaultsAsync(PermissionSettings settings, bool allowBypass)
    {
        var config = new JsonFileStore<AppConfig>(Path.Combine(_temp.Root, "config.json"), NullLogger<JsonFileStore<AppConfig>>.Instance, watch: false);
        _disposables.Add(config);
        var defaults = new PermissionDefaults(config, _paths);
        await defaults.SaveAsync(settings, allowBypass, TestContext.Current.CancellationToken);
        return defaults;
    }

    private SessionService Create(IChatClient client, ITitleGenerator? titles = null, PermissionDefaults? defaults = null)
    {
        var store = new SessionStore(_paths, NullLoggerFactory.Instance);
        var service = new SessionService(store, (_, _) => Task.FromResult(client), NullLoggerFactory.Instance, titles: titles, defaults: defaults);
        _disposables.Add(service);
        _disposables.Add(store);
        return service;
    }
}
