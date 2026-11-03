using BlazeHarness.Core.Tools;
using BlazeHarness.Core.Tools.Web;
using BlazeHarness.Core.Web.Browser;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Unit.Tools.Web;

public sealed class BrowserNavigateToolTests
{
    private const string Chat = "chat1";

    private readonly IBrowserEngine _engine = Substitute.For<IBrowserEngine>();
    private readonly BrowserNavigateTool _sut;

    public BrowserNavigateToolTests()
    {
        _sut = new BrowserNavigateTool(_engine, Chat, 5380);
    }

    [Fact]
    public void Describe_LocalPage_GrantsHostAndPort()
    {
        // Arrange
        using var arguments = JsonDocument.Parse("""{"url":"http://localhost:3000/a"}""");

        // Act
        var result = _sut.Describe(arguments.RootElement);

        // Assert
        result.Should().NotBeNull();
        result.Target.Should().Be("localhost");
        result.GrantKey.Should().Be("browser:localhost:3000");
        result.Reason.Should().Contain("http://localhost:3000/a");
    }

    [Theory]
    [InlineData("""{"url":"https://example.com/"}""")]
    [InlineData("""{"url":"http://localhost:5380/"}""")]
    [InlineData("""{"url":"not a url"}""")]
    [InlineData("{}")]
    public void Describe_RefusedOrMalformed_ReturnsNull(string json)
    {
        // Arrange
        using var arguments = JsonDocument.Parse(json);

        // Act
        var result = _sut.Describe(arguments.RootElement);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_Page_RendersIt()
    {
        // Arrange
        var view = new BrowserPageView(new Uri("http://localhost:3000/"), "Home", 200, "- heading \"Hi\"", false, ["console error: boom"]);
        _engine.NavigateAsync(Chat, new Uri("http://localhost:3000/"), Arg.Any<CancellationToken>()).Returns(view);

        // Act
        var result = await _sut.ExecuteAsync(ToolArguments.Parse("""{"url":"http://localhost:3000/"}"""), new ToolContext(Path.GetTempPath()), TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().Contain("URL: http://localhost:3000/ (status 200)").And.Contain("Title: Home").And.Contain("console error: boom").And.Contain("heading \"Hi\"");
    }

    [Fact]
    public async Task ExecuteAsync_EngineRefuses_ReturnsErrorWithItsMessage()
    {
        // Arrange
        _engine.NavigateAsync(Chat, Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns<BrowserPageView>(_ => throw new BrowserException("nothing is listening"));

        // Act
        var result = await _sut.ExecuteAsync(ToolArguments.Parse("""{"url":"http://localhost:3000/"}"""), new ToolContext(Path.GetTempPath()), TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Be("nothing is listening");
    }

    [Fact]
    public async Task ExecuteAsync_BadUrl_ReturnsErrorWithoutCallingEngine()
    {
        // Act
        var result = await _sut.ExecuteAsync(ToolArguments.Parse("""{"url":"nope"}"""), new ToolContext(Path.GetTempPath()), TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeTrue();
        await _engine.DidNotReceiveWithAnyArgs().NavigateAsync(default!, default!, TestContext.Current.CancellationToken);
    }
}
