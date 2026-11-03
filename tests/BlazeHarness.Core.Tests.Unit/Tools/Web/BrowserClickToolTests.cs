using BlazeHarness.Core.Tools;
using BlazeHarness.Core.Tools.Web;
using BlazeHarness.Core.Web.Browser;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Unit.Tools.Web;

public sealed class BrowserClickToolTests
{
    private const string Chat = "chat1";

    private readonly IBrowserEngine _engine = Substitute.For<IBrowserEngine>();
    private readonly BrowserClickTool _sut;

    public BrowserClickToolTests()
    {
        _sut = new BrowserClickTool(_engine, Chat);
    }

    [Fact]
    public void Describe_Always_NeedsNoApprovalOfItsOwn()
    {
        // Arrange
        using var arguments = JsonDocument.Parse("""{"ref":"e1"}""");

        // Act
        var result = _sut.Describe(arguments.RootElement);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_Ref_ClicksAndRendersThePage()
    {
        // Arrange
        var view = new BrowserPageView(new Uri("http://localhost:3000/x"), "T", null, "- text: \"Saved\"", false, []);
        _engine.ClickAsync(Chat, "e42", Arg.Any<CancellationToken>()).Returns(view);

        // Act
        var result = await _sut.ExecuteAsync(ToolArguments.Parse("""{"ref":"e42"}"""), new ToolContext(Path.GetTempPath()), TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().Contain("Saved");
    }

    [Fact]
    public async Task ExecuteAsync_ElementGone_ReturnsError()
    {
        // Arrange
        _engine.ClickAsync(Chat, "e42", Arg.Any<CancellationToken>()).Returns<BrowserPageView>(_ => throw new BrowserException("gone"));

        // Act
        var result = await _sut.ExecuteAsync(ToolArguments.Parse("""{"ref":"e42"}"""), new ToolContext(Path.GetTempPath()), TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(ToolResult.Error("gone"));
    }
}
