using BlazeHarness.Core.Sessions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace BlazeHarness.Core.Tests.Integration.Sessions;

public sealed class SessionStoreTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "bh-sessions-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly SessionStore _sut;

    public SessionStoreTests()
    {
        _paths = new AppPaths(_home, _home);
        _sut = Create();
    }

    public void Dispose()
    {
        _sut.Dispose();
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAsync_WritesMetadataAndIndexesTheSession()
    {
        // Act
        var info = await _sut.CreateAsync("/work/app", TestContext.Current.CancellationToken);

        // Assert
        File.Exists(Path.Combine(_paths.SessionDirectory(info.Id), "session.json")).Should().BeTrue();
        info.CreatedAt.Should().Be(_time.GetUtcNow());
        info.FolderPath.Should().Be("/work/app");
        _sut.Find(info.Id).Should().Be(info);
    }

    [Fact]
    public async Task Constructor_ExistingSessions_AreLoadedFromDisk()
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
    public async Task Constructor_UnreadableMetadata_SkipsThatSession()
    {
        // Arrange
        var good = await _sut.CreateAsync(cancellationToken: TestContext.Current.CancellationToken);
        var bad = Path.Combine(_paths.SessionsDirectory, "broken");
        Directory.CreateDirectory(bad);
        await File.WriteAllTextAsync(Path.Combine(bad, "session.json"), "{ not json", TestContext.Current.CancellationToken);

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
    public async Task UpdateAsync_UnknownSession_ReturnsNull()
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
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => _sut.UpdateAsync(info.Id, s => s with { Title = s.Title + "x" }, cancellationToken: ct)));

        // Assert
        _sut.Find(info.Id)!.Title.Should().Be(new string('x', 20));
        Directory.GetFiles(_paths.SessionDirectory(info.Id)).Select(Path.GetFileName).Should().BeEquivalentTo("session.json");
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheWholeSessionFolderAndIndexEntry()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var info = await _sut.CreateAsync(cancellationToken: ct);
        await _sut.GetLog(info.Id).AppendAsync([new UserMessageEvent(DateTimeOffset.UnixEpoch, Guid.NewGuid(), "hi")], ct);
        Directory.CreateDirectory(Path.Combine(_paths.SessionDirectory(info.Id), "outputs"));

        // Act
        await _sut.DeleteAsync(info.Id, ct);

        // Assert
        _sut.Find(info.Id).Should().BeNull();
        Directory.GetDirectories(_paths.SessionsDirectory).Should().BeEmpty();
    }

    [Fact]
    public async Task Constructor_LeftoverTrashFolder_IsRemoved()
    {
        // Arrange
        var trash = Path.Combine(_paths.SessionsDirectory, ".trash-abc");
        Directory.CreateDirectory(trash);
        await File.WriteAllTextAsync(Path.Combine(trash, "session.json"), "{}", TestContext.Current.CancellationToken);

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
    public async Task GetLog_ReturnsTheSameInstanceForASession()
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

    private SessionStore Create()
    {
        return new SessionStore(_paths, NullLoggerFactory.Instance, _time);
    }
}
