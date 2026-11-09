using BlazeHarness.Core.Attachments;
using BlazeHarness.Core.Tools;
using BlazeHarness.Core.Tools.Web;
using BlazeHarness.Core.Web.Browser;

namespace BlazeHarness.Core.Tests.Integration.Tools.Web;

public sealed class BrowserScreenshotToolTests : IDisposable
{
    private const string Chat = "chat1";

    private readonly TempRoot _root = new();
    private readonly IBrowserEngine _engine = Substitute.For<IBrowserEngine>();
    private readonly BrowserScreenshotTool _sut;

    public BrowserScreenshotToolTests()
    {
        _sut = new BrowserScreenshotTool(_engine, Chat);
    }

    public void Dispose()
    {
        _root.Dispose();
        _engine.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_Page_WritesThePngInsideTheFolder()
    {
        // Arrange
        _engine.ScreenshotAsync(Chat, true, Arg.Any<CancellationToken>()).Returns([1, 2, 3]);

        // Act
        var result = await Run(new { path = "shots/home.png", full_page = true });

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().Contain("shots/home.png");
        (await File.ReadAllBytesAsync(Path.Combine(_root.Root, "shots", "home.png"), TestContext.Current.CancellationToken)).Should().Equal(1, 2, 3);
        var image = result.Images.Should().ContainSingle().Subject.Content;
        image.MediaType.Should().Be("image/png");
        image.Data.ToArray().Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task ExecuteAsync_PictureTooLargeForTheModel_SavesItWithoutAnImage()
    {
        // Arrange
        _engine.ScreenshotAsync(Chat, true, Arg.Any<CancellationToken>()).Returns(new byte[ImageFiles.MaxViewableBytes + 1]);

        // Act
        var result = await Run(new { path = "big.png", full_page = true });

        // Assert
        result.IsError.Should().BeFalse();
        result.Images.Should().BeEmpty();
        result.Output.Should().Contain("too large to show you");
        File.Exists(Path.Combine(_root.Root, "big.png")).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_NotPng_ReturnsError()
    {
        // Act
        var result = await Run(new { path = "home.jpg" });

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain(".png");
        await _engine.DidNotReceiveWithAnyArgs().ScreenshotAsync(default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_PathOutsideFolder_ReturnsError()
    {
        // Act
        var result = await Run(new { path = "../home.png" });

        // Assert
        result.IsError.Should().BeTrue();
        await _engine.DidNotReceiveWithAnyArgs().ScreenshotAsync(default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_NoPage_ReturnsEngineMessage()
    {
        // Arrange
        _engine.ScreenshotAsync(Chat, false, Arg.Any<CancellationToken>()).Returns<byte[]>(_ => throw new BrowserException("No page is open."));

        // Act
        var result = await Run(new { path = "home.png" });

        // Assert
        result.Should().Be(ToolResult.Error("No page is open."));
    }

    private Task<ToolResult> Run(object args) => _sut.ExecuteAsync(TempRoot.Args(args), _root.Context, TestContext.Current.CancellationToken);
}
