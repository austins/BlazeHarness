using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Tests.Unit.Tools;

public sealed class SendToChatToolTests
{
    private readonly ToolContext _context = new(Path.GetTempPath());
    private readonly List<(string Chat, string? Folder, string Message)> _resolved = [];
    private readonly List<SendToChatPlan> _delivered = [];
    private readonly SendToChatTool _sut;

    public SendToChatToolTests()
    {
        _sut = new SendToChatTool(
            (chat, folder, message) =>
            {
                _resolved.Add((chat, folder, message));
                return new SendToChatResolution(new SendToChatPlan("abc", "Story 2", null, SameScope: true, message));
            },
            (plan, _) =>
            {
                _delivered.Add(plan);
                return Task.FromResult(ToolResult.Ok("sent"));
            });
    }

    [Fact]
    public async Task ExecuteAsync_ValidArguments_ResolvesAndDelivers()
    {
        // Act
        var result = await RunAsync("""{"chat":"Story 2","message":"go","folder":"none"}""");

        // Assert
        result.IsError.Should().BeFalse();
        _resolved.Should().ContainSingle().Which.Should().Be(("Story 2", "none", "go"));
        _delivered.Should().ContainSingle().Which.Message.Should().Be("go");
    }

    [Theory]
    [InlineData("""{"chat":"","message":"go"}""")]
    [InlineData("""{"chat":"abc","message":"  "}""")]
    [InlineData("""{"message":"go"}""")]
    public async Task ExecuteAsync_MissingArguments_ReturnsAnErrorAndDeliversNothing(string json)
    {
        // Act
        var result = await RunAsync(json);

        // Assert
        result.IsError.Should().BeTrue();
        _delivered.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_MessageOverTheLimit_ReturnsAnError()
    {
        // Arrange
        var message = new string('x', SendToChatPlan.MaxMessageLength + 1);

        // Act
        var result = await RunAsync($$"""{"chat":"abc","message":"{{message}}"}""");

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain("shorten");
        _delivered.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_UnresolvedChat_ReturnsTheResolverError()
    {
        // Arrange
        var sut = new SendToChatTool((_, _, _) => new SendToChatResolution(null, "No such chat."), (_, _) => throw new InvalidOperationException());

        // Act
        var result = await sut.ExecuteAsync(ToolArguments.Parse("""{"chat":"x","message":"go"}"""), _context, TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Be("No such chat.");
    }

    [Fact]
    public void Reason_NewChat_NamesTheFolderAndLeavesTheMessageToTheArguments()
    {
        // Arrange
        var plan = new SendToChatPlan(null, null, "/work", SameScope: true, "line one\nline two");

        // Act
        var reason = plan.Reason;

        // Assert
        plan.Creates.Should().BeTrue();
        reason.Should().Contain("a new chat").And.Contain("/work").And.NotContain("line one");
    }

    private Task<ToolResult> RunAsync(string json) => _sut.ExecuteAsync(ToolArguments.Parse(json), _context, TestContext.Current.CancellationToken);
}
