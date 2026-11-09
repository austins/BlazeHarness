using BlazeHarness.Core.Chats.Events;
using BlazeHarness.Core.Chats.Titles;
using BlazeHarness.Core.Tests.Integration.Config;
using BlazeHarness.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using System.Runtime.Versioning;
using ChatStore = BlazeHarness.Core.Chats.ChatStore;

namespace BlazeHarness.Core.Tests.Integration.Chats;

public sealed class ChatStoreTests : IDisposable
{
    private readonly TempFolder _temp = new("bh-chats-");
    private readonly AppPaths _paths;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly ChatStore _sut;

    public ChatStoreTests()
    {
        _paths = new AppPaths(_temp.Root, _temp.Root);
        _sut = Create();
    }

    public static bool CanRestrictDirectories => !OperatingSystem.IsWindows() && Environment.UserName != "root";

    public void Dispose()
    {
        _sut.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task CreateAsync_WritesMetadataAndIndexesTheChat()
    {
        // Act
        var info = await _sut.CreateAsync("/work/app", TestContext.Current.CancellationToken);

        // Assert
        File.Exists(Path.Combine(_paths.ChatDirectory(info.Id), "chat.json")).Should().BeTrue();
        new FileInfo(Path.Combine(_paths.ChatDirectory(info.Id), "chat.json")).ShouldBeOwnerOnly();
        info.CreatedAt.Should().Be(_time.GetUtcNow());
        info.FolderPath.Should().Be("/work/app");
        _sut.Find(info.Id).Should().Be(info);
    }

    [Fact]
    public async Task Constructor_ExistingChats_AreLoadedFromDisk()
    {
        // Arrange
        var info = await _sut.CreateAsync(cancellationToken: TestContext.Current.CancellationToken);
        await _sut.RenameAsync(info.Id, "Saved", TestContext.Current.CancellationToken);

        // Act
        using var reopened = Create();

        // Assert
        reopened.List().Should().ContainSingle().Which.Title.Should().Be("Saved");
    }

    [Fact]
    public async Task Constructor_UnreadableMetadata_SkipsThatChat()
    {
        // Arrange
        var good = await _sut.CreateAsync(cancellationToken: TestContext.Current.CancellationToken);
        var bad = Path.Combine(_paths.ChatsDirectory, "broken");
        Directory.CreateDirectory(bad);
        await File.WriteAllTextAsync(Path.Combine(bad, "chat.json"), "{ not json", TestContext.Current.CancellationToken);

        // Act
        using var reopened = Create();

        // Assert
        reopened.List().Should().ContainSingle().Which.Id.Should().Be(good.Id);
    }

    [Fact]
    public async Task List_OrdersByMostRecentActivity()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var first = await _sut.CreateAsync(cancellationToken: ct);
        _time.Advance(TimeSpan.FromMinutes(1));
        var second = await _sut.CreateAsync(cancellationToken: ct);
        _time.Advance(TimeSpan.FromMinutes(1));

        // Act
        await _sut.UpdateAsync(first.Id, s => s, touch: true, ct);

        // Assert
        _sut.List().Select(s => s.Id).Should().Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task List_WithTitleFilter_MatchesCaseInsensitively()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var apples = await _sut.CreateAsync(cancellationToken: ct);
        var pears = await _sut.CreateAsync(cancellationToken: ct);
        await _sut.RenameAsync(apples.Id, "Apple pie recipe", ct);
        await _sut.RenameAsync(pears.Id, "Pears", ct);

        // Act
        var matches = _sut.List("  APPLE ");

        // Assert
        matches.Should().ContainSingle().Which.Id.Should().Be(apples.Id);
    }

    [Fact]
    public async Task RenameAsync_SetsTheTitleAsUserChosen()
    {
        // Arrange
        var info = await _sut.CreateAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var renamed = await _sut.RenameAsync(info.Id, "  My chat  ", TestContext.Current.CancellationToken);

        // Assert
        renamed.Should().NotBeNull();
        renamed.Title.Should().Be("My chat");
        renamed.TitleSource.Should().Be(TitleSource.User);
    }

    [Fact]
    public async Task UpdateAsync_CannotChangeTheId()
    {
        // Arrange
        var info = await _sut.CreateAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var updated = await _sut.UpdateAsync(info.Id, s => s with { Id = "other" }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        updated.Should().NotBeNull();
        updated.Id.Should().Be(info.Id);
        _sut.Find("other").Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_UnknownChat_ReturnsNull()
    {
        // Act
        var updated = await _sut.UpdateAsync("missing", s => s, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        updated.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_Concurrent_DoesNotLoseUpdatesOrLeaveTempFiles()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var info = await _sut.CreateAsync(cancellationToken: ct);

        // Act
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => _sut.UpdateAsync(info.Id, s => s with { Title = s.Title + "x" }, cancellationToken: ct)));

        // Assert
        _sut.Find(info.Id)!.Title.Should().Be(new string('x', 20));
        Directory.GetFiles(_paths.ChatDirectory(info.Id)).Select(Path.GetFileName).Should().BeEquivalentTo("chat.json");
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheWholeChatFolderAndIndexEntry()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var info = await _sut.CreateAsync(cancellationToken: ct);
        await _sut.GetLog(info.Id).AppendAsync([new UserMessageEvent(DateTimeOffset.UnixEpoch, Guid.NewGuid(), "hi")], ct);
        Directory.CreateDirectory(Path.Combine(_paths.ChatDirectory(info.Id), "outputs"));

        // Act
        await _sut.DeleteAsync(info.Id, ct);

        // Assert
        _sut.Find(info.Id).Should().BeNull();
        Directory.GetDirectories(_paths.ChatsDirectory).Should().BeEmpty();
    }

    [Fact(Skip = "Needs POSIX directory permissions that the user is subject to.", SkipUnless = nameof(CanRestrictDirectories))]
    [UnsupportedOSPlatform("windows")]
    public async Task DeleteAsync_MoveFails_KeepsTheChatListed()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var info = await _sut.CreateAsync(cancellationToken: ct);
        File.SetUnixFileMode(_paths.ChatsDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            // Act
            var act = async () => await _sut.DeleteAsync(info.Id, ct);

            // Assert
            await act.Should().ThrowAsync<Exception>();
            _sut.Find(info.Id).Should().NotBeNull();
        }
        finally
        {
            File.SetUnixFileMode(_paths.ChatsDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task Constructor_LeftoverTrashFolder_IsRemoved()
    {
        // Arrange
        var trash = Path.Combine(_paths.ChatsDirectory, ".trash-abc");
        Directory.CreateDirectory(trash);
        await File.WriteAllTextAsync(Path.Combine(trash, "chat.json"), "{}", TestContext.Current.CancellationToken);

        // Act
        using var reopened = Create();

        // Assert
        Directory.Exists(trash).Should().BeFalse();
        reopened.List().Should().BeEmpty();
    }

    [Fact]
    public async Task Changed_IsRaisedForCreateUpdateAndDelete()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var raised = 0;
        _sut.Changed += (_, _) => raised++;

        // Act
        var info = await _sut.CreateAsync(cancellationToken: ct);
        await _sut.RenameAsync(info.Id, "x", ct);
        await _sut.DeleteAsync(info.Id, ct);

        // Assert
        raised.Should().Be(3);
    }

    [Fact]
    public async Task GetLog_ReturnsTheSameInstanceForAChat()
    {
        // Arrange
        var info = await _sut.CreateAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Act
#pragma warning disable IDISP001 // The store owns the log.
        var log = _sut.GetLog(info.Id);
#pragma warning restore IDISP001

        // Assert
        _sut.GetLog(info.Id).Should().BeSameAs(log);
    }

    [Fact]
    public async Task RepointFolderAsync_MovesOnlyTheChatsInThatFolder()
    {
        // Arrange
        var inOld = await _sut.CreateAsync("/work/old", TestContext.Current.CancellationToken);
        var elsewhere = await _sut.CreateAsync("/work/other", TestContext.Current.CancellationToken);

        // Act
        var moved = await _sut.RepointFolderAsync("/work/old", "/work/new", TestContext.Current.CancellationToken);

        // Assert
        moved.Should().Be(1);
        _sut.Find(inOld.Id)!.FolderPath.Should().Be("/work/new");
        _sut.Find(elsewhere.Id)!.FolderPath.Should().Be("/work/other");
    }

    [Fact]
    public async Task RepointFolderAsync_NullTarget_DetachesTheChatsAndSavesThat()
    {
        // Arrange
        var info = await _sut.CreateAsync("/work/old", TestContext.Current.CancellationToken);

        // Act
        await _sut.RepointFolderAsync("/work/old", null, TestContext.Current.CancellationToken);

        // Assert
        _sut.Find(info.Id)!.FolderPath.Should().BeNull();
        using var reopened = Create();
        reopened.Find(info.Id)!.FolderPath.Should().BeNull();
    }

    private ChatStore Create()
    {
        return new ChatStore(_paths, NullLoggerFactory.Instance, _time);
    }
}
