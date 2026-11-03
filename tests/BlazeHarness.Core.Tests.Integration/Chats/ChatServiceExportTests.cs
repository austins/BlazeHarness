using BlazeHarness.Core.Chats;
using BlazeHarness.Core.Chats.Events;
using BlazeHarness.Core.Chats.Export;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Tests.Shared.Agent;
using ChatService = BlazeHarness.Core.Chats.ChatService;

#pragma warning disable IDISP001 // The service disposes the chats it opens.

namespace BlazeHarness.Core.Tests.Integration.Chats;

public sealed class ChatServiceExportTests : IDisposable
{
    private readonly ChatServiceRig _rig = new("bh-svc-export-", "Sunny.");

    public ChatServiceExportTests()
    {
        _rig.Build();
    }

    private ChatService Sut => _rig.Service;

    public void Dispose() => _rig.Dispose();

    [Fact]
    public async Task ExportAsync_UnknownChat_ReturnsNull()
    {
        // Act
        var file = await Sut.ExportAsync("missing", ExportFormat.Json, TestContext.Current.CancellationToken);

        // Assert
        file.Should().BeNull();
    }

    [Fact]
    public async Task ImportAsync_ExportedChat_CreatesAnEquivalentChatWithoutAFolder()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(_rig.Root, "work");
        Directory.CreateDirectory(folder);
        var source = await Sut.CreateAsync(folder, ct);
        await Sut.SendAsync(source.Id, "What is the weather?", TestTargets.Create(), cancellationToken: TestContext.Current.CancellationToken);
        var exported = await Sut.ExportAsync(source.Id, ExportFormat.Json, ct);

        // Act
        var imported = await Sut.ImportAsync(exported!.Content, ct);

        // Assert
        imported.Id.Should().NotBe(source.Id);
        imported.FolderPath.Should().BeNull();
        imported.Title.Should().Be("What is the weather?");
        imported.ProviderId.Should().Be("p1");
        imported.Access.Should().Be(AccessLevel.ReadOnly);
        var original = await _rig.Store.GetLog(source.Id).ReadAsync(ct);
        (await _rig.Store.GetLog(imported.Id).ReadAsync(ct)).Select(e => e.GetType()).Should().Equal(original.Select(e => e.GetType()));
    }

    [Fact]
    public async Task ImportAsync_ImportedChat_OpensWithItsMessages()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var source = await Sut.CreateAsync(cancellationToken: ct);
        await Sut.SendAsync(source.Id, "What is the weather?", TestTargets.Create(), cancellationToken: TestContext.Current.CancellationToken);
        var exported = await Sut.ExportAsync(source.Id, ExportFormat.Json, ct);

        // Act
        var imported = await Sut.ImportAsync(exported!.Content, ct);
        var chat = await Sut.OpenAsync(imported.Id, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        chat!.Entries.Select(e => e.Text).Should().Equal("What is the weather?", "Sunny.");
    }

    [Fact]
    public async Task ImportAsync_FileWithPermissionsAndAFolder_DoesNotGrantThem()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var json = ChatExport.ToJson(
            new ChatInfo { Title = "Hostile", FolderPath = "/", Access = AccessLevel.Unrestricted, Approvals = ApprovalMode.NeverAsk },
            [new UserMessageEvent(DateTimeOffset.UnixEpoch, Guid.NewGuid(), "hi"), new PermissionsChangedEvent(DateTimeOffset.UnixEpoch, AccessLevel.Unrestricted, ApprovalMode.NeverAsk)],
            DateTimeOffset.UnixEpoch);

        // Act
        var imported = await Sut.ImportAsync(json, ct);

        // Assert
        imported.FolderPath.Should().BeNull();
        imported.Access.Should().Be(AccessLevel.ReadOnly);
        imported.Approvals.Should().BeNull();
    }

    [Fact]
    public async Task ImportAsync_NotAChatExport_ThrowsAndCreatesNothing()
    {
        // Act
        var act = () => Sut.ImportAsync("{}", TestContext.Current.CancellationToken);

        // Assert
        (await act.Should().ThrowAsync<ChatImportException>()).Which.Error.Should().Be(ChatImportError.NotAnExport);
        _rig.Store.List().Should().BeEmpty();
    }
}
