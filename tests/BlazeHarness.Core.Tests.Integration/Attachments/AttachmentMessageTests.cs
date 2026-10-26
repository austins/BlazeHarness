using BlazeHarness.Core.Attachments;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Integration.Attachments;

public sealed class AttachmentMessageTests : IDisposable
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "bh-attachmsg-" + Guid.NewGuid().ToString("N"));
    private readonly SessionAttachments _store;

    public AttachmentMessageTests()
    {
        _store = new SessionAttachments(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Build_NoAttachments_IsPlainText()
    {
        // Act
        var message = AttachmentMessage.Build("hello", [], _store);

        // Assert
        message.Role.Should().Be(ChatRole.User);
        message.Text.Should().Be("hello");
        message.Contents.Should().ContainSingle();
    }

    [Fact]
    public async Task Build_InlineText_AddsAFencedBlockNamingTheFile()
    {
        // Arrange
        var info = await SaveAsync("a.cs", "int x;\n");

        // Act
        var message = AttachmentMessage.Build("look", [new SentAttachment(info, AttachmentDelivery.Inline)], _store);

        // Assert
        message.Text.Should().Be("look\n\nAttached file a.cs:\n```\nint x;\n```");
    }

    [Fact]
    public async Task Build_InlineTextWithBackticks_UsesALongerFence()
    {
        // Arrange
        var info = await SaveAsync("doc.md", "```js\ncode\n```\n");

        // Act
        var message = AttachmentMessage.Build(string.Empty, [new SentAttachment(info, AttachmentDelivery.Inline)], _store);

        // Assert
        message.Text.Should().StartWith("Attached file doc.md:\n````\n```js").And.EndWith("```\n````");
    }

    [Fact]
    public async Task Build_OnDemandText_NamesTheFileAndTheTool()
    {
        // Arrange
        var info = await SaveAsync("big.log", string.Join('\n', Enumerable.Range(0, 300)));

        // Act
        var message = AttachmentMessage.Build("see", [new SentAttachment(info, AttachmentDelivery.OnDemand)], _store);

        // Assert
        message.Text.Should().Contain($"id {info.Id}").And.Contain("300 lines").And.Contain("read_attachment").And.StartWith("see\n\n");
        message.Text.Should().NotContain("299");
    }

    [Fact]
    public async Task Build_Image_AddsImageContentAndANote()
    {
        // Arrange
        var info = await SaveAsync("pic.png", PngBytes);

        // Act
        var message = AttachmentMessage.Build("what is this?", [new SentAttachment(info, AttachmentDelivery.Image)], _store);

        // Assert
        message.Text.Should().Contain("[Attached image: pic.png]");
        var image = message.Contents.OfType<DataContent>().Should().ContainSingle().Subject;
        image.MediaType.Should().Be("image/png");
        image.Data.ToArray().Should().Equal(PngBytes);
    }

    [Fact]
    public void Build_FileThatIsGone_SaysSo()
    {
        // Arrange
        var gone = new AttachmentInfo("0123abcd", "gone.txt", "text/plain", 3, 1);

        // Act
        var message = AttachmentMessage.Build("x", [new SentAttachment(gone, AttachmentDelivery.Inline)], _store);

        // Assert
        message.Text.Should().Contain("'gone.txt' is no longer available");
    }

    [Fact]
    public async Task OmitImages_ReplacesImageContentWithANoteAndKeepsTheRest()
    {
        // Arrange
        var info = await SaveAsync("pic.png", PngBytes);
        List<ChatMessage> messages =
        [
            new(ChatRole.System, "sys"),
            AttachmentMessage.Build("look", [new SentAttachment(info, AttachmentDelivery.Image)], _store),
        ];

        // Act
        AttachmentMessage.OmitImages(messages);

        // Assert
        messages[0].Text.Should().Be("sys");
        messages[1].Contents.OfType<DataContent>().Should().BeEmpty();
        messages[1].Text.Should().Contain("look").And.Contain("this model can't view images");
    }

    [Theory]
    [InlineData(500L, "500 bytes")]
    [InlineData(2048L, "2 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(3L * 1024 * 1024, "3 MB")]
    public void FormatSize_Bytes_ReadsNaturally(long bytes, string expected)
    {
        // Act
        var text = AttachmentMessage.FormatSize(bytes);

        // Assert
        text.Should().Be(expected);
    }

    private Task<AttachmentInfo> SaveAsync(string name, string content) => SaveAsync(name, System.Text.Encoding.UTF8.GetBytes(content));

    private async Task<AttachmentInfo> SaveAsync(string name, byte[] content)
    {
        await using var stream = new MemoryStream(content);
        return await _store.SaveAsync(name, stream, TestContext.Current.CancellationToken);
    }
}
