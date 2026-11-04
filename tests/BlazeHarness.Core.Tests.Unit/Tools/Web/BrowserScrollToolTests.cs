using BlazeHarness.Core.Tools;
using BlazeHarness.Core.Tools.Web;
using BlazeHarness.Core.Web.Browser;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Unit.Tools.Web;

public sealed class BrowserScrollToolTests
{
    private const string Chat = "chat1";

    private readonly IBrowserEngine _engine = Substitute.For<IBrowserEngine>();
    private readonly BrowserScrollTool _sut;

    public BrowserScrollToolTests()
    {
        _sut = new BrowserScrollTool(_engine, Chat);
    }

    [Fact]
    public void Describe_Always_NeedsNoApprovalOfItsOwn()
    {
        // Arrange
        using var arguments = JsonDocument.Parse("""{"direction":"down"}""");

        // Act
        var result = _sut.Describe(arguments.RootElement);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("down", "Down")]
    [InlineData("Up", "Up")]
    [InlineData(" top ", "Top")]
    [InlineData("BOTTOM", "Bottom")]
    public async Task ExecuteAsync_Direction_ScrollsAndRendersThePage(string direction, string expectedName)
    {
        // Arrange
        var expected = Enum.Parse<BrowserScrollDirection>(expectedName);
        var view = new BrowserPageView(new Uri("http://localhost:3000/x"), "T", null, "- text: \"More items\"", false, []);
        _engine.ScrollAsync(Chat, expected, Arg.Any<CancellationToken>()).Returns(view);

        // Act
        var result = await _sut.ExecuteAsync(ToolArguments.Parse($$"""{"direction":"{{direction}}"}"""), new ToolContext(Path.GetTempPath()), TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().Contain("More items");
    }

    [Theory]
    [InlineData("sideways")]
    [InlineData("7")]
    public async Task ExecuteAsync_UnknownDirection_ReturnsErrorWithoutScrolling(string direction)
    {
        // Act
        var result = await _sut.ExecuteAsync(ToolArguments.Parse($$"""{"direction":"{{direction}}"}"""), new ToolContext(Path.GetTempPath()), TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeTrue();
        await _engine.DidNotReceiveWithAnyArgs().ScrollAsync(default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_NoPageOpen_ReturnsError()
    {
        // Arrange
        _engine.ScrollAsync(Chat, BrowserScrollDirection.Down, Arg.Any<CancellationToken>()).Returns<BrowserPageView>(_ => throw new BrowserException("no page"));

        // Act
        var result = await _sut.ExecuteAsync(ToolArguments.Parse("""{"direction":"down"}"""), new ToolContext(Path.GetTempPath()), TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(ToolResult.Error("no page"));
    }
}
