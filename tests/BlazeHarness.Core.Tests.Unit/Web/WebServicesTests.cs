using BlazeHarness.Core.Tools.Web;
using BlazeHarness.Core.Web;
using BlazeHarness.Core.Web.Browser;
using BlazeHarness.Tests.Shared;

namespace BlazeHarness.Core.Tests.Unit.Web;

public sealed class WebServicesTests : IDisposable
{
    private readonly IBrowserEngine _engine = Substitute.For<IBrowserEngine>();
    private readonly HttpClient _client = new();
    private WebSettings _settings = new();

    public void Dispose()
    {
        _client.Dispose();
        _engine.Dispose();
    }

    [Fact]
    public void ToolsFor_Defaults_IncludesBrowserTools()
    {
        // Arrange
        using var sut = Create();

        // Act
        var names = sut.ToolsFor("c1").Select(t => t.Name).ToList();

        // Assert
        names.Should().Equal("web_search", "web_fetch", BrowserNavigateTool.ToolName, BrowserSnapshotTool.ToolName, BrowserClickTool.ToolName, BrowserTypeTool.ToolName, BrowserScrollTool.ToolName, BrowserWaitTool.ToolName, BrowserScreenshotTool.ToolName, BrowserDownloadTool.ToolName);
    }

    [Fact]
    public void ToolsFor_BrowserOff_LeavesBrowserToolsOut()
    {
        // Arrange
        _settings = new WebSettings { BrowserEnabled = false };
        using var sut = Create();

        // Act
        var names = sut.ToolsFor("c1").Select(t => t.Name).ToList();

        // Assert
        names.Should().Equal("web_search", "web_fetch");
    }

    [Fact]
    public void ToolsFor_BrowserNotAllowed_LeavesBrowserToolsOut()
    {
        // Arrange
        using var sut = Create();

        // Act
        var names = sut.ToolsFor("c1", browserAllowed: false).Select(t => t.Name).ToList();

        // Assert
        names.Should().Equal("web_search", "web_fetch");
    }

    [Fact]
    public void Forget_Chat_ClosesItsBrowserPage()
    {
        // Arrange
        using var sut = Create();

        // Act
        sut.Forget("c1");

        // Assert
        _engine.Received(1).Forget("c1");
    }

    [Fact]
    public async Task IsBrowserInstalledAsync_Engine_ReportsItsAnswer()
    {
        // Arrange
        _engine.IsInstalledAsync(Arg.Any<CancellationToken>()).Returns(true);
        using var sut = Create();

        // Act
        var result = await sut.IsBrowserInstalledAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Dispose_Always_DisposesEngine()
    {
        // Arrange
        var sut = Create();

        // Act
#pragma warning disable IDISP017 // Disposing explicitly is the behavior under test.
        sut.Dispose();
#pragma warning restore IDISP017

        // Assert
        _engine.Received(1).Dispose();
    }

    private WebServices Create()
    {
        var loggers = Substitute.For<ILoggerFactory>();
        loggers.CreateLogger(Arg.Any<string>()).Returns(new LoggerFake<WebServices>());
        return new WebServices(() => _settings, _client, loggers, TimeProvider.System, 5380, _engine);
    }
}
