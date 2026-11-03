using BlazeHarness.Core.Tools;
using BlazeHarness.Core.Tools.Web;
using BlazeHarness.Core.Web.Browser;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Unit.Tools.Web;

public sealed class BrowserTypeToolTests
{
    private const string Chat = "chat1";

    private readonly IBrowserEngine _engine = Substitute.For<IBrowserEngine>();
    private readonly BrowserTypeTool _sut;

    public BrowserTypeToolTests()
    {
        _sut = new BrowserTypeTool(_engine, Chat);
    }

    [Fact]
    public void Describe_Always_NeedsNoApprovalOfItsOwn()
    {
        // Arrange
        using var arguments = JsonDocument.Parse("""{"ref":"e1","text":"x"}""");

        // Act
        var result = _sut.Describe(arguments.RootElement);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"ref":"e7","text":"Ada"}""", false)]
    [InlineData("""{"ref":"e7","text":"Ada","submit":true}""", true)]
    public async Task ExecuteAsync_Arguments_PassTextAndSubmitToTheEngine(string json, bool submit)
    {
        // Arrange
        var view = new BrowserPageView(new Uri("http://localhost:3000/x"), string.Empty, null, "- text: \"Hi Ada\"", false, []);
        _engine.TypeAsync(Chat, "e7", "Ada", submit, Arg.Any<CancellationToken>()).Returns(view);

        // Act
        var result = await _sut.ExecuteAsync(ToolArguments.Parse(json), new ToolContext(Path.GetTempPath()), TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().Contain("Hi Ada");
    }

    [Fact]
    public async Task ExecuteAsync_FieldRefused_ReturnsError()
    {
        // Arrange
        _engine.TypeAsync(Chat, "e7", "x", false, Arg.Any<CancellationToken>()).Returns<BrowserPageView>(_ => throw new BrowserException("not a field"));

        // Act
        var result = await _sut.ExecuteAsync(ToolArguments.Parse("""{"ref":"e7","text":"x"}"""), new ToolContext(Path.GetTempPath()), TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(ToolResult.Error("not a field"));
    }
}
