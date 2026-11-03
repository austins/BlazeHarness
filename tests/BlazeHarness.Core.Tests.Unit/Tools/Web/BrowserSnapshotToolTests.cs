using BlazeHarness.Core.Tools;
using BlazeHarness.Core.Tools.Web;
using BlazeHarness.Core.Web.Browser;

namespace BlazeHarness.Core.Tests.Unit.Tools.Web;

public sealed class BrowserSnapshotToolTests
{
    private const string Chat = "chat1";

    private readonly IBrowserEngine _engine = Substitute.For<IBrowserEngine>();
    private readonly BrowserSnapshotTool _sut;

    public BrowserSnapshotToolTests()
    {
        _sut = new BrowserSnapshotTool(_engine, Chat);
    }

    [Fact]
    public void IsReadOnly_Always_IsTrue()
    {
        // Assert
        _sut.IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_PageOpen_RendersIt()
    {
        // Arrange
        var view = new BrowserPageView(new Uri("http://localhost:3000/x"), string.Empty, null, "- button \"Save\"", false, []);
        _engine.SnapshotAsync(Chat, Arg.Any<CancellationToken>()).Returns(view);

        // Act
        var result = await _sut.ExecuteAsync(ToolArguments.Parse("{}"), new ToolContext(Path.GetTempPath()), TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().Contain("URL: http://localhost:3000/x").And.NotContain("status").And.NotContain("Title:").And.Contain("button \"Save\"");
    }

    [Fact]
    public async Task ExecuteAsync_NoPage_ReturnsError()
    {
        // Arrange
        _engine.SnapshotAsync(Chat, Arg.Any<CancellationToken>()).Returns<BrowserPageView>(_ => throw new BrowserException("No page is open."));

        // Act
        var result = await _sut.ExecuteAsync(ToolArguments.Parse("{}"), new ToolContext(Path.GetTempPath()), TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Be("No page is open.");
    }
}
