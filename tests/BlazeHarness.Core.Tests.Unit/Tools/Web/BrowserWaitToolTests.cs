using BlazeHarness.Core.Tools;
using BlazeHarness.Core.Tools.Web;
using BlazeHarness.Core.Web.Browser;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Unit.Tools.Web;

public sealed class BrowserWaitToolTests
{
    private const string Chat = "chat1";

    private readonly IBrowserEngine _engine = Substitute.For<IBrowserEngine>();
    private readonly BrowserWaitTool _sut;

    public BrowserWaitToolTests()
    {
        _sut = new BrowserWaitTool(_engine, Chat);
    }

    [Fact]
    public void Describe_Always_NeedsNoApprovalOfItsOwn()
    {
        // Arrange
        using var arguments = JsonDocument.Parse("{}");

        // Act
        var result = _sut.Describe(arguments.RootElement);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_NoArguments_WaitsTheDefaultTimeWithoutText()
    {
        // Arrange
        var view = new BrowserPageView(new Uri("http://localhost:3000/x"), "T", null, "- text: \"Built\"", false, []);
        _engine.WaitAsync(Chat, TimeSpan.FromSeconds(BrowserWaitTool.DefaultSeconds), null, Arg.Any<CancellationToken>()).Returns(view);

        // Act
        var result = await _sut.ExecuteAsync(ToolArguments.Parse("{}"), new ToolContext(Path.GetTempPath()), TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().Contain("Built");
    }

    [Fact]
    public async Task ExecuteAsync_SecondsAndText_PassesBothOn()
    {
        // Arrange
        var view = new BrowserPageView(new Uri("http://localhost:3000/x"), "T", null, "- text: \"Ready\"", false, []);
        _engine.WaitAsync(Chat, TimeSpan.FromSeconds(12), "Ready", Arg.Any<CancellationToken>()).Returns(view);

        // Act
        var result = await _sut.ExecuteAsync(ToolArguments.Parse("""{"seconds":12,"text":"Ready"}"""), new ToolContext(Path.GetTempPath()), TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeFalse();
        await _engine.Received(1).WaitAsync(Chat, TimeSpan.FromSeconds(12), "Ready", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("31")]
    public async Task ExecuteAsync_SecondsOutOfRange_Throws(string seconds)
    {
        // Act
        var act = async () => await _sut.ExecuteAsync(ToolArguments.Parse($$"""{"seconds":{{seconds}}}"""), new ToolContext(Path.GetTempPath()), TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ToolArgumentException>();
    }

    [Fact]
    public async Task ExecuteAsync_NoPageOpen_ReturnsError()
    {
        // Arrange
        _engine.WaitAsync(Chat, Arg.Any<TimeSpan>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns<BrowserPageView>(_ => throw new BrowserException("no page"));

        // Act
        var result = await _sut.ExecuteAsync(ToolArguments.Parse("{}"), new ToolContext(Path.GetTempPath()), TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(ToolResult.Error("no page"));
    }
}
