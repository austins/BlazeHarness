using BlazeHarness.Core.Tools;
using BlazeHarness.Core.Tools.Web;
using BlazeHarness.Core.Web.Browser;

namespace BlazeHarness.Core.Tests.Integration.Tools.Web;

public sealed class BrowserDownloadToolTests : IDisposable
{
    private const string Chat = "chat1";

    private readonly TempRoot _root = new();
    private readonly IBrowserEngine _engine = Substitute.For<IBrowserEngine>();
    private readonly BrowserDownloadTool _sut;

    public BrowserDownloadToolTests()
    {
        _sut = new BrowserDownloadTool(_engine, Chat);
    }

    public void Dispose()
    {
        _root.Dispose();
        _engine.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_Link_AsksTheEngineToSaveInsideTheFolder()
    {
        // Arrange
        var expected = Path.Combine(_root.Root, "out", "r.txt");
        _engine.DownloadAsync(Chat, "e5", expected, Arg.Any<CancellationToken>()).Returns(new BrowserDownload("report.txt", 5));

        // Act
        var result = await Run(new { @ref = "e5", path = "out/r.txt" });

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().Contain("report.txt").And.Contain("out/r.txt");
    }

    [Fact]
    public async Task ExecuteAsync_PathOutsideFolder_ReturnsError()
    {
        // Act
        var result = await Run(new { @ref = "e5", path = "../r.txt" });

        // Assert
        result.IsError.Should().BeTrue();
        await _engine.DidNotReceiveWithAnyArgs().DownloadAsync(default!, default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_DownloadFails_ReturnsEngineMessage()
    {
        // Arrange
        _engine.DownloadAsync(Chat, "e5", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns<BrowserDownload>(_ => throw new BrowserException("Nothing was downloaded."));

        // Act
        var result = await Run(new { @ref = "e5", path = "r.txt" });

        // Assert
        result.Should().Be(ToolResult.Error("Nothing was downloaded."));
    }

    private Task<ToolResult> Run(object args) => _sut.ExecuteAsync(TempRoot.Args(args), _root.Context, TestContext.Current.CancellationToken);
}
