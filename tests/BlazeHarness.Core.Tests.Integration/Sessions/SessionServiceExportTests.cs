using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Providers;
using BlazeHarness.Core.Sessions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable IDISP001 // The service disposes the sessions it opens.

namespace BlazeHarness.Core.Tests.Integration.Sessions;

public sealed class SessionServiceExportTests : IDisposable
{
    private readonly TempFolder _temp = new("bh-svc-export-");
    private readonly SessionStore _store;
    private readonly ScriptedChatClient _client = new([ScriptedChatClient.Text("Sunny.")]);
    private readonly SessionService _sut;

    public SessionServiceExportTests()
    {
        var paths = new AppPaths(Path.Combine(_temp.Root, "app"), _temp.Root);
        _store = new SessionStore(paths, NullLoggerFactory.Instance);
        _sut = new SessionService(_store, (_, _) => Task.FromResult<IChatClient>(_client), NullLoggerFactory.Instance);
    }

    public void Dispose()
    {
        _sut.Dispose();
        _store.Dispose();
        _client.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task ExportAsync_UnknownSession_ReturnsNull()
    {
        // Act
        var file = await _sut.ExportAsync("missing", ExportFormat.Json, TestContext.Current.CancellationToken);

        // Assert
        file.Should().BeNull();
    }

    [Fact]
    public async Task ImportAsync_ExportedChat_CreatesAnEquivalentSessionWithoutAFolder()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(_temp.Root, "work");
        Directory.CreateDirectory(folder);
        var source = await _sut.CreateAsync(folder, ct);
        await _sut.SendAsync(source.Id, "What is the weather?", Target());
        var exported = await _sut.ExportAsync(source.Id, ExportFormat.Json, ct);

        // Act
        var imported = await _sut.ImportAsync(exported!.Content, ct);

        // Assert
        imported.Id.Should().NotBe(source.Id);
        imported.FolderPath.Should().BeNull();
        imported.Title.Should().Be("What is the weather?");
        imported.ProviderId.Should().Be("p1");
        imported.Access.Should().Be(AccessLevel.ReadOnly);
        var original = await _store.GetLog(source.Id).ReadAsync(ct);
        (await _store.GetLog(imported.Id).ReadAsync(ct)).Select(e => e.GetType()).Should().Equal(original.Select(e => e.GetType()));
    }

    [Fact]
    public async Task ImportAsync_ImportedChat_OpensWithItsMessages()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var source = await _sut.CreateAsync(cancellationToken: ct);
        await _sut.SendAsync(source.Id, "What is the weather?", Target());
        var exported = await _sut.ExportAsync(source.Id, ExportFormat.Json, ct);

        // Act
        var imported = await _sut.ImportAsync(exported!.Content, ct);
        var session = await _sut.OpenAsync(imported.Id);

        // Assert
        session!.Entries.Select(e => e.Text).Should().Equal("What is the weather?", "Sunny.");
    }

    [Fact]
    public async Task ImportAsync_FileWithPermissionsAndAFolder_DoesNotGrantThem()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var json = SessionExport.ToJson(
            new SessionInfo { Title = "Hostile", FolderPath = "/", Access = AccessLevel.Unrestricted, Approvals = ApprovalMode.NeverAsk },
            [new UserMessageEvent(DateTimeOffset.UnixEpoch, Guid.NewGuid(), "hi"), new PermissionsChangedEvent(DateTimeOffset.UnixEpoch, AccessLevel.Unrestricted, ApprovalMode.NeverAsk)],
            DateTimeOffset.UnixEpoch);

        // Act
        var imported = await _sut.ImportAsync(json, ct);

        // Assert
        imported.FolderPath.Should().BeNull();
        imported.Access.Should().Be(AccessLevel.ReadOnly);
        imported.Approvals.Should().BeNull();
    }

    [Fact]
    public async Task ImportAsync_NotAChatExport_ThrowsAndCreatesNothing()
    {
        // Act
        var act = () => _sut.ImportAsync("{}", TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<FormatException>();
        _store.List().Should().BeEmpty();
    }

    private static ChatTarget Target()
    {
        var modelConfig = new ModelConfig { Id = "fast", ContextWindow = 8000, MaxOutputTokens = 1000 };
        var providerConfig = new ProviderConfig { Id = "p1", DisplayName = "P1", BaseUrl = "http://localhost/v1", Models = [modelConfig] };
        return new ChatTarget(providerConfig, modelConfig, null);
    }
}
