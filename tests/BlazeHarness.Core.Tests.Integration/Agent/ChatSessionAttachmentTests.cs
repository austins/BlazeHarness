using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Attachments;
using BlazeHarness.Core.Providers;
using BlazeHarness.Core.Sessions;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Integration.Agent;

public sealed class ChatSessionAttachmentTests : IDisposable
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "bh-sessattach-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingSessionLog _log = new();
    private readonly SessionAttachments _attachments;

    public ChatSessionAttachmentTests()
    {
        _attachments = new SessionAttachments(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task SendAsync_WithATextFile_PutsItInTheMessageAndRecordsTheDelivery()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        using var sut = Create(client);
        var file = await _attachments.SaveAsync("a.txt", "hello file");

        // Act
        await sut.SendAsync("read this", [file], TestTargets.Create());

        // Assert
        var request = client.Requests.Single().Single(m => m.Role == ChatRole.User);
        request.Text.Should().Be("read this\n\nAttached file a.txt:\n```\nhello file\n```");
        sut.Entries[0].Text.Should().Be("read this");
        sut.Entries[0].Attachments.Should().Equal(new SentAttachment(file, AttachmentDelivery.Inline));
        _log.Events.OfType<UserMessageEvent>().Single().Attachments.Should().Equal(new SentAttachment(file, AttachmentDelivery.Inline));
    }

    [Fact]
    public async Task SendAsync_WithOnlyAFile_StillSends()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        using var sut = Create(client);
        var file = await _attachments.SaveAsync("a.txt", "x");

        // Act
        await sut.SendAsync(string.Empty, [file], TestTargets.Create());

        // Assert
        sut.Entries.Should().HaveCount(2);
        sut.Entries[0].Text.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_WithAnImageForAnImageModel_SendsImageContent()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("a cat")]);
        using var sut = Create(client);
        var file = await _attachments.SaveAsync("cat.png", PngBytes);

        // Act
        await sut.SendAsync("what is it?", [file], TestTargets.Create(input: InputTypes.Text | InputTypes.Image));

        // Assert
        client.Requests.Single().Single(m => m.Role == ChatRole.User).Contents.OfType<DataContent>().Should().ContainSingle();
    }

    [Fact]
    public async Task SendAsync_WithAnImageForATextOnlyModel_SendsANoteInsteadAndKeepsTheHistoryIntact()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("can't see")]);
        using var sut = Create(client);
        var file = await _attachments.SaveAsync("cat.png", PngBytes);

        // Act
        await sut.SendAsync("what is it?", [file], TestTargets.Create());

        // Assert
        var sent = client.Requests.Single().Single(m => m.Role == ChatRole.User);
        sent.Contents.OfType<DataContent>().Should().BeEmpty();
        sent.Text.Should().Contain("this model can't view images");
        sut.History[0].Contents.OfType<DataContent>().Should().ContainSingle();
    }

    [Fact]
    public async Task SendAsync_TooManyFiles_Throws()
    {
        // Act
        var act = async () =>
        {
            using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
            using var sut = Create(client);
            var file = await _attachments.SaveAsync("a.txt", "x");
            await sut.SendAsync("x", [.. Enumerable.Repeat(file, SessionAttachments.MaxFilesPerMessage + 1)], TestTargets.Create());
        };

        // Assert
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*at most*");
    }

    [Fact]
    public async Task SendAsync_FileFromAnotherSession_Throws()
    {
        // Act
        var act = async () =>
        {
            using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
            using var sut = Create(client);
            await sut.SendAsync("x", [new AttachmentInfo("0123abcd", "x.txt", "text/plain", 1, 1)], TestTargets.Create());
        };

        // Assert
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*not saved*");
    }

    [Fact]
    public async Task Restore_MessageWithAttachments_RebuildsTheSameHistoryMessage()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        using var first = Create(client);
        var file = await _attachments.SaveAsync("a.txt", "hello file");
        await first.SendAsync("read this", [file], TestTargets.Create());
        using var restored = Create(client);

        // Act
        restored.Restore(_log.Events);

        // Assert
        restored.Entries[0].Attachments.Should().Equal(first.Entries[0].Attachments);
        restored.History[0].Text.Should().Be(first.History[0].Text);
    }

    [Fact]
    public async Task EditAsync_KeepsTheAttachmentsOfTheEditedMessage()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("one")]).Enqueue([ScriptedChatClient.Text("two")]);
        using var sut = Create(client);
        var file = await _attachments.SaveAsync("a.txt", "hello file");
        await sut.SendAsync("first", [file], TestTargets.Create());

        // Act
        await sut.EditAsync(sut.Entries[0].Id, "second", TestTargets.Create());

        // Assert
        sut.Entries[0].Attachments.Should().ContainSingle().Which.File.Should().Be(file);
        client.Requests[1].Single(m => m.Role == ChatRole.User).Text.Should().Contain("second").And.Contain("hello file");
        _log.Events.OfType<UserMessageEvent>().Last().Attachments.Should().ContainSingle();
    }

    [Fact]
    public async Task SendAsync_FilesAddUpToMoreThanTheMessageLimit_Throws()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        using var sut = Create(client);
        var half = new AttachmentInfo("a", "a.txt", "text/plain", (SessionAttachments.MaxMessageBytes / 2) + 1, 1);
        var other = half with { Id = "b", Name = "b.txt" };

        // Act
        var act = async () => await sut.SendAsync("x", [half, other], TestTargets.Create());

        // Assert
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*at most 50 MB*");
        sut.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task EditAsync_ToAModelWithASmallerWindow_PlansTheFilesAgainForTheNewModel()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("one")]).Enqueue([ScriptedChatClient.Text("two")]);
        using var sut = Create(client);
        var file = await _attachments.SaveAsync("big.txt", new string('x', 20_000));
        await sut.SendAsync("first", [file], Target(1_000_000));

        // Act
        await sut.EditAsync(sut.Entries[0].Id, "second", Target(8000));

        // Assert
        sut.Entries[0].Attachments.Should().Equal(new SentAttachment(file, AttachmentDelivery.OnDemand));
        client.Requests[1].Single(m => m.Role == ChatRole.User).Text.Should().Contain("read_attachment").And.NotContain("xxxxxxxxxx");
        _log.Events.OfType<UserMessageEvent>().Last().Attachments.Should().Equal(new SentAttachment(file, AttachmentDelivery.OnDemand));
    }

    [Fact(Skip = "Needs a named pipe, which Windows cannot make with mkfifo.", SkipWhen = nameof(IsWindows))]
    public async Task SendAsync_WhileAFileIsBeingRead_DoesNotBlockReadersOfTheSession()
    {
        // Arrange
        using var client = new ScriptedChatClient([ScriptedChatClient.Text("ok")]);
        using var sut = Create(client);
        var file = await _attachments.SaveAsync("a.txt", "x");
        var path = _attachments.Find(file.Id)!.Path;
        File.Delete(path);
#pragma warning disable IDISP013 // The process is only disposed after the awaits that wait for it.
        using var mkfifo = System.Diagnostics.Process.Start("mkfifo", path);
        await mkfifo.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var target = TestTargets.Create();
        var send = Task.Run(() => sut.SendAsync("read", [file], target), TestContext.Current.CancellationToken);

        // Opening the write end only returns once the session has started reading the file.
        await using var writer = await Task.Run(() => new FileStream(path, FileMode.Open, FileAccess.Write), TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
#pragma warning restore IDISP013

        // Act
        var status = await Task.Run(() => sut.GetContextStatus(target), TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await writer.DisposeAsync();
        await send.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // Assert
        status.Should().NotBeNull();
        sut.Entries.Should().HaveCount(2);
    }

    public static bool IsWindows => OperatingSystem.IsWindows();

    private static ChatTarget Target(int contextWindow)
    {
        var model = new ModelConfig { Id = "m", ContextWindow = contextWindow, MaxOutputTokens = 1000 };
        return new ChatTarget(new ProviderConfig { Id = "p", DisplayName = "P", BaseUrl = "http://localhost/v1", Models = [model] }, model, null);
    }

    private ChatSession Create(ScriptedChatClient client)
    {
        return new ChatSession((_, _) => Task.FromResult<IChatClient>(client), _log)
        {
            Attachments = _attachments,
        };
    }
}
