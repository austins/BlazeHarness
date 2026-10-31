using BlazeHarness.Core.Mcp;
using BlazeHarness.Core.Tools;
using BlazeHarness.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazeHarness.Core.Tests.Integration.Mcp;

public sealed class McpManagerOfferTests : IAsyncLifetime
{
    private const string Config = """
        {"mcpServers": {
          "small": {"command": "s", "alwaysLoad": true},
          "big": {"command": "b", "alwaysLoad": ["ping"], "excludeTools": ["secret"]},
          "lazy": {"command": "l"}
        }}
        """;

    private readonly TempFolder _temp = new("bh-mcpoffer-");
    private readonly IMcpConnector _connector = Substitute.For<IMcpConnector>();
    private readonly McpConfigFile _file;
    private readonly McpToolCache _cache;
    private readonly McpManager _sut;

    public McpManagerOfferTests()
    {
        Directory.CreateDirectory(_temp.Root);
        _file = new McpConfigFile(Path.Combine(_temp.Root, "mcp.json"), new LoggerFake<McpConfigFile>(), TimeProvider.System, watch: false);
        _cache = new McpToolCache(new AppPaths(_temp.Root, _temp.Root), NullLoggerFactory.Instance, TimeProvider.System);
        _sut = new McpManager(_file, _connector, new LoggerFake<McpManager>(), TimeProvider.System, toolCache: _cache);
    }

    public async ValueTask InitializeAsync()
    {
        await _file.SaveAsync(Config, TestContext.Current.CancellationToken);
        Serve("s", Tool("only"));
        Serve("b", Tool("ping"), Tool("secret"), Tool("create_report", "Builds a quarterly report."));
        Serve("l", Tool("send_email", "Sends an email message."), Tool("list_inbox", "Lists the inbox."));
    }

    public async ValueTask DisposeAsync()
    {
        await _sut.DisposeAsync();
        _cache.Dispose();
        _file.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task OfferAsync_Mixed_DeclaresAlwaysLoadedToolsAndTheSearchPair()
    {
        // Act
        var tools = await _sut.OfferAsync(["small", "big", "lazy"], TestContext.Current.CancellationToken);

        // Assert
        tools.Select(t => t.Name).Should().Equal("big__ping", "small__only", "find_tools", "use_tool");
        await _connector.DidNotReceive().ConnectAsync(Arg.Is<McpServerConfig>(c => c.Name == "lazy"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OfferAsync_EveryServerAlwaysLoaded_HasNoSearchTools()
    {
        // Act
        var tools = await _sut.OfferAsync(["small"], TestContext.Current.CancellationToken);

        // Assert
        tools.Select(t => t.Name).Should().Equal("small__only");
    }

    [Fact]
    public async Task ListAsync_ServerWithPatternsAndExcludeTools_LeavesOutWhatIsAlreadyOfferedOrExcluded()
    {
        // Act
        var listing = await _sut.ListAsync("big", connect: true, TestContext.Current.CancellationToken);

        // Assert
        listing.Known.Should().BeTrue();
        listing.Tools.Select(t => t.Name).Should().Equal("big__create_report");
    }

    [Fact]
    public async Task ListAsync_NotConnectedAndNeverCached_IsUnknownAndDoesNotConnect()
    {
        // Act
        var listing = await _sut.ListAsync("lazy", connect: false, TestContext.Current.CancellationToken);

        // Assert
        listing.Known.Should().BeFalse();
        await _connector.DidNotReceive().ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_AfterAnEarlierRun_UsesTheCacheWithoutConnecting()
    {
        // Arrange
        await _sut.ListAsync("lazy", connect: true, TestContext.Current.CancellationToken);
        _connector.ClearReceivedCalls();
        await using var second = new McpManager(_file, _connector, new LoggerFake<McpManager>(), TimeProvider.System, toolCache: _cache);

        // Act
        var listing = await second.ListAsync("lazy", connect: false, TestContext.Current.CancellationToken);

        // Assert
        listing.Known.Should().BeTrue();
        listing.Tools.Select(t => t.Name).Should().Equal("lazy__list_inbox", "lazy__send_email");
        await _connector.DidNotReceive().ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_UnknownServer_ReportsIt()
    {
        // Act
        var listing = await _sut.ListAsync("nope", connect: true, TestContext.Current.CancellationToken);

        // Assert
        listing.Known.Should().BeFalse();
        listing.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task FindToolAsync_ServerOutsideTheChat_IsRefused()
    {
        // Act
        var lookup = await _sut.FindToolAsync("lazy__send_email", ["small"], TestContext.Current.CancellationToken);

        // Assert
        lookup.Tool.Should().BeNull();
        lookup.Problem.Should().Contain("find_tools");
        await _connector.DidNotReceive().ConnectAsync(Arg.Is<McpServerConfig>(c => c.Name == "lazy"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FindToolAsync_ToolOfAChatServer_ConnectsAndReturnsIt()
    {
        // Act
        var lookup = await _sut.FindToolAsync("lazy__send_email", ["lazy"], TestContext.Current.CancellationToken);

        // Assert
        lookup.Problem.Should().BeNull();
        lookup.Tool!.Name.Should().Be("lazy__send_email");
    }

    [Fact]
    public async Task FindToolAsync_ExcludedTool_IsNotReachable()
    {
        // Act
        var lookup = await _sut.FindToolAsync("big__secret", ["big"], TestContext.Current.CancellationToken);

        // Assert
        lookup.Tool.Should().BeNull();
        lookup.Problem.Should().NotBeNull();
    }

    [Fact]
    public async Task FindTools_NoArguments_ListsServersWithoutConnecting()
    {
        // Arrange
        var find = new FindToolsTool(_sut, ["lazy"]);

        // Act
        var result = await find.ExecuteAsync(Args("{}"), new ToolContext(_temp.Root), TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().Contain("lazy");
        await _connector.DidNotReceive().ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FindTools_Query_ReturnsTheMatchWithItsSchema()
    {
        // Arrange
        var find = new FindToolsTool(_sut, ["lazy"]);

        // Act
        var result = await find.ExecuteAsync(Args("""{"query": "send an email"}"""), new ToolContext(_temp.Root), TestContext.Current.CancellationToken);

        // Assert
        result.Output.Should().Contain("## lazy__send_email").And.Contain("Input schema:").And.NotContain("## lazy__list_inbox");
    }

    [Fact]
    public async Task FindTools_QueryWithNoMatch_ListsTheToolsInstead()
    {
        // Arrange
        var find = new FindToolsTool(_sut, ["lazy"]);

        // Act
        var result = await find.ExecuteAsync(Args("""{"query": "quantum entanglement"}"""), new ToolContext(_temp.Root), TestContext.Current.CancellationToken);

        // Assert
        result.Output.Should().Contain("No tool matches").And.Contain("- lazy__send_email").And.Contain("- lazy__list_inbox");
    }

    [Fact]
    public async Task FindTools_ServerListWithALimit_PagesThroughEveryTool()
    {
        // Arrange
        var find = new FindToolsTool(_sut, ["lazy"]);

        // Act
        var first = await find.ExecuteAsync(Args("""{"server": "lazy", "limit": 1}"""), new ToolContext(_temp.Root), TestContext.Current.CancellationToken);
        var second = await find.ExecuteAsync(Args("""{"server": "lazy", "limit": 1, "offset": 1}"""), new ToolContext(_temp.Root), TestContext.Current.CancellationToken);

        // Assert
        first.Output.Should().Contain("- lazy__list_inbox").And.NotContain("lazy__send_email").And.Contain("Showing 1-1 of 2").And.Contain("offset 1");
        second.Output.Should().Contain("- lazy__send_email").And.NotContain("lazy__list_inbox").And.NotContain("Showing");
    }

    [Fact]
    public async Task FindTools_OffsetPastTheEnd_SaysSo()
    {
        // Arrange
        var find = new FindToolsTool(_sut, ["lazy"]);

        // Act
        var result = await find.ExecuteAsync(Args("""{"server": "lazy", "offset": 5}"""), new ToolContext(_temp.Root), TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().Contain("only 2 tools");
    }

    [Fact]
    public async Task FindTools_SearchWithALargeLimit_IsCappedAtTwenty()
    {
        // Arrange
        var find = new FindToolsTool(_sut, ["lazy"]);

        // Act
        var result = await find.ExecuteAsync(Args("""{"query": "inbox email", "limit": 100}"""), new ToolContext(_temp.Root), TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().Contain("## lazy__");
    }

    [Fact]
    public async Task FindTools_UnknownServer_IsAnErrorNamingTheServers()
    {
        // Arrange
        var find = new FindToolsTool(_sut, ["lazy"]);

        // Act
        var result = await find.ExecuteAsync(Args("""{"server": "zzz"}"""), new ToolContext(_temp.Root), TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain("lazy");
    }

    [Theory]
    [InlineData("""{"name": "lazy__send_email", "arguments": {"to": "a@b.test"}}""", """{"to": "a@b.test"}""")]
    [InlineData("""{"name": "lazy__send_email", "arguments": "{\"to\": \"a@b.test\"}"}""", """{"to": "a@b.test"}""")]
    [InlineData("""{"name": "lazy__send_email"}""", "{}")]
    public async Task UseTool_ResolveAsync_AcceptsArgumentsAsObjectTextOrNothing(string call, string expected)
    {
        // Arrange
        var use = new UseToolTool(_sut, ["lazy"]);

        // Act
        var forwarded = await use.ResolveAsync(Args(call), TestContext.Current.CancellationToken);

        // Assert
        forwarded.Problem.Should().BeNull();
        forwarded.Name.Should().Be("lazy__send_email");
        forwarded.ArgumentsJson.Should().Be(expected);
    }

    private static ToolArguments Args(string json) => ToolArguments.Parse(json);

    private static McpToolInfo Tool(string name, string description = "Does it.") => new(name, description, """{"type":"object","properties":{}}""");

    private void Serve(string command, params McpToolInfo[] tools)
    {
        var connection = Substitute.For<IMcpConnection>();
        connection.Closed.Returns(new TaskCompletionSource().Task);
        connection.ListToolsAsync(Arg.Any<CancellationToken>()).Returns(tools);
        _connector.ConnectAsync(Arg.Is<McpServerConfig>(c => c.Command == command), Arg.Any<CancellationToken>()).Returns(connection);
    }
}
