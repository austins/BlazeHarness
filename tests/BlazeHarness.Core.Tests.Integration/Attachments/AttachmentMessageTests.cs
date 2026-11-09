using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Attachments;
using BlazeHarness.Core.Tools;
using BlazeHarness.Tests.Shared;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Integration.Attachments;

public sealed class AttachmentMessageTests : IDisposable
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private readonly TempFolder _temp = new("bh-attachmsg-");
    private readonly ChatAttachments _store;

    public AttachmentMessageTests()
    {
        _store = new ChatAttachments(_temp.Root);
    }

    public void Dispose()
    {
        _temp.Dispose();
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
        var info = await _store.SaveAsync("a.cs", "int x;\n");

        // Act
        var message = AttachmentMessage.Build("look", [new SentAttachment(info, AttachmentDelivery.Inline)], _store);

        // Assert
        message.Text.Should().Be("look\n\nAttached file a.cs:\n```\nint x;\n```");
    }

    [Fact]
    public async Task Build_InlineTextWithBackticks_UsesALongerFence()
    {
        // Arrange
        var info = await _store.SaveAsync("doc.md", "```js\ncode\n```\n");

        // Act
        var message = AttachmentMessage.Build(string.Empty, [new SentAttachment(info, AttachmentDelivery.Inline)], _store);

        // Assert
        message.Text.Should().StartWith("Attached file doc.md:\n````\n```js").And.EndWith("```\n````");
    }

    [Fact]
    public async Task Build_OnDemandText_NamesTheFileAndTheTool()
    {
        // Arrange
        var info = await _store.SaveAsync("big.log", string.Join('\n', Enumerable.Range(0, 300)));

        // Act
        var message = AttachmentMessage.Build("see", [new SentAttachment(info, AttachmentDelivery.OnDemand)], _store);

        // Assert
        message.Text.Should().Contain($"id {info.Id}").And.Contain("300 lines").And.Contain("read_attachment").And.StartWith("see\n\n");
        // The id is random hex, so look for the line rather than the digits.
        message.Text.Should().NotContain("\n299");
    }

    [Fact]
    public async Task Build_Image_AddsImageContentAndANote()
    {
        // Arrange
        var info = await _store.SaveAsync("pic.png", PngBytes);

        // Act
        var message = AttachmentMessage.Build("what is this?", [new SentAttachment(info, AttachmentDelivery.Image)], _store);

        // Assert
        message.Text.Should().Contain($"[Attached image: pic.png, id {info.Id}, path {_store.Find(info.Id)!.Path}]");
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
        var info = await _store.SaveAsync("pic.png", PngBytes);
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

    [Fact]
    public void ForToolImages_NoCallHasAnImage_ReturnsNull()
    {
        // Act
        var message = AttachmentMessage.ForToolImages([("read", "c1", [])]);

        // Assert
        message.Should().BeNull();
    }

    [Fact]
    public void ForToolImages_PictureNoLongerSaved_SaysSo()
    {
        // Arrange
        var call = new ToolCallEntry("c1", "browser_screenshot", "{}", "x") { Images = [new AttachmentInfo("0123abcd", "browser_screenshot.png", "image/png", 3, 0)] };

        // Act
        var message = AttachmentMessage.ForToolImages([call], _store);

        // Assert
        message!.Text.Should().Be("<tool_response>\n[Picture from browser_screenshot call c1, attachment 0123abcd is no longer available.]\n</tool_response>");
        StepMessage.Is(message).Should().BeTrue();
        message.Contents.OfType<DataContent>().Should().BeEmpty();
    }

    [Fact]
    public void ForToolImages_PicturesPastTheCallBudget_NamesThemWithTheirIds()
    {
        // Arrange
        const long Size = 4L * 1024 * 1024;
        var pictures = Enumerable.Range(0, 6)
            .Select(i => new ToolImage(new DataContent(new byte[] { 1 }, "image/png"), new AttachmentInfo($"0000000{i}", "shot.png", "image/png", Size, 0)))
            .ToList();

        // Act
        var message = AttachmentMessage.ForToolImages([("shot", "c1", pictures)]);

        // Assert
        message!.Contents.OfType<DataContent>().Should().HaveCount(5);
        message.Text.Should().Contain("[Picture 1 of 6 from shot call c1, attachment 00000000:]")
            .And.Contain("[Picture 6 of 6 from shot call c1, attachment 00000005 was not shown, to keep the pictures of one call under 20 MB. Read it with read_attachment and the id 00000005 if you need it.]");
    }

    [Fact]
    public void ForToolImages_PictureTooLargeToView_IsNamedInstead()
    {
        // Arrange
        var info = new AttachmentInfo("0123abcd", "shot.png", "image/png", ImageFiles.MaxViewableBytes + 1, 0);

        // Act
        var message = AttachmentMessage.ForToolImages([("shot", "c1", [new ToolImage(new DataContent(new byte[] { 1 }, "image/png"), info)])]);

        // Assert
        message!.Contents.OfType<DataContent>().Should().BeEmpty();
        message.Text.Should().Contain("[Picture from shot call c1, attachment 0123abcd was not shown:").And.Contain("larger than the 5 MB a picture can be.]");
    }

    [Fact]
    public void WithoutToolImages_PictureWithoutAnAttachment_KeepsTheLabelInANote()
    {
        // Arrange
        var message = AttachmentMessage.ForToolImages([("shot", "c1", [new ToolImage(new DataContent(PngBytes, "image/png"))])])!;

        // Act
        var stripped = AttachmentMessage.WithoutToolImages(message);

        // Assert
        stripped!.Contents.OfType<DataContent>().Should().BeEmpty();
        stripped.Text.Should().Be("<tool_response>\n[Picture from shot call c1 was removed to save context.]\n</tool_response>");
        StepMessage.Is(stripped).Should().BeTrue();
    }

    [Fact]
    public void WithoutToolImages_UserMessage_ReturnsNull()
    {
        // Arrange
        var message = new ChatMessage(ChatRole.User, [new TextContent("look"), new DataContent(PngBytes, "image/png")]);

        // Act
        var stripped = AttachmentMessage.WithoutToolImages(message);

        // Assert
        stripped.Should().BeNull();
    }
}
