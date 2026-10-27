using BlazeHarness.Core.Config;
using BlazeHarness.Core.Mcp;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Tools;
using Microsoft.Extensions.Time.Testing;

namespace BlazeHarness.Core.Tests.Integration.Mcp;

public sealed class McpManagerTests : IAsyncLifetime
{
    private const string Two = """{"mcpServers": {"alpha": {"command": "a"}, "beta": {"command": "b"}}}""";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bh-mcpm-" + Guid.NewGuid().ToString("N"));
    private readonly IMcpConnector _connector = Substitute.For<IMcpConnector>();
    private readonly FakeTimeProvider _time = new();
    private readonly Dictionary<string, string> _environment = [];
    private readonly CancellationTokenSource _cancel = new();
    private readonly McpConfigFile _file;
    private readonly JsonFileStore<AppConfig> _appConfig;
    private readonly PermissionDefaults _permissions;
    private readonly McpManager _sut;

    public McpManagerTests()
    {
        Directory.CreateDirectory(_dir);
        _file = new McpConfigFile(Path.Combine(_dir, "mcp.json"), new LoggerFake<McpConfigFile>(), watch: false);
        _appConfig = new JsonFileStore<AppConfig>(Path.Combine(_dir, "config.json"), new LoggerFake<McpManagerTests>(), watch: false);
        _permissions = new PermissionDefaults(_appConfig, new AppPaths(Path.Combine(_dir, "app"), _dir));
        _sut = new McpManager(_file, _connector, new LoggerFake<McpManager>(), name => _environment.GetValueOrDefault(name), _time, TimeSpan.FromSeconds(10), _permissions);
    }

    public async ValueTask InitializeAsync() => await _file.SaveAsync(Two, TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        _cancel.Dispose();
        await _sut.DisposeAsync();
        _file.Dispose();
        _appConfig.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task ToolsAsync_EnabledServer_ConnectsAndReturnsNamespacedTools()
    {
        // Arrange
        var alpha = Connection(Tool("read"), Tool("write"));
        _connector.ConnectAsync(Arg.Is<McpServerConfig>(c => c.Name == "alpha"), Arg.Any<CancellationToken>()).Returns(alpha);

        // Act
        var tools = await _sut.ToolsAsync(["alpha"], TestContext.Current.CancellationToken);

        // Assert
        tools.Select(t => t.Name).Should().Equal("alpha__read", "alpha__write");
        tools.Should().AllBeAssignableTo<IMcpTool>().Which.Select(t => t.ServerName).Should().OnlyContain(n => n == "alpha");
        _sut.Statuses().Single(s => s.Config.Name == "alpha").State.Should().Be(McpServerState.Connected);
        _sut.Statuses().Single(s => s.Config.Name == "beta").State.Should().Be(McpServerState.Stopped);
    }

    [Fact]
    public async Task ToolsAsync_CalledAgain_KeepsTheConnection()
    {
        // Arrange
        _connector.ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>()).Returns(_ => Connection(Tool("read")));

        // Act
        await _sut.ToolsAsync(["alpha"], TestContext.Current.CancellationToken);
        await _sut.ToolsAsync(["alpha"], TestContext.Current.CancellationToken);

        // Assert
        await _connector.Received(1).ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ToolsAsync_TwoServersWithTheSameTool_KeepsBoth()
    {
        // Arrange
        _connector.ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>()).Returns(_ => Connection(Tool("search")));

        // Act
        var tools = await _sut.ToolsAsync(["beta", "alpha"], TestContext.Current.CancellationToken);

        // Assert
        tools.Select(t => t.Name).Should().Equal("alpha__search", "beta__search");
    }

    [Fact]
    public async Task ToolsAsync_NothingWanted_ConnectsToNothing()
    {
        // Act
        var tools = await _sut.ToolsAsync(["unknown"], TestContext.Current.CancellationToken);

        // Assert
        tools.Should().BeEmpty();
        await _connector.DidNotReceive().ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ToolsAsync_ConnectFails_LeavesTheServerOutAndKeepsTheReason()
    {
        // Arrange
        _connector.ConnectAsync(Arg.Is<McpServerConfig>(c => c.Name == "alpha"), Arg.Any<CancellationToken>()).Returns<IMcpConnection>(_ => throw new McpConnectException("npx not found"));
        _connector.ConnectAsync(Arg.Is<McpServerConfig>(c => c.Name == "beta"), Arg.Any<CancellationToken>()).Returns(_ => Connection(Tool("ok")));

        // Act
        var tools = await _sut.ToolsAsync(["alpha", "beta"], TestContext.Current.CancellationToken);

        // Assert
        tools.Select(t => t.Name).Should().Equal("beta__ok");
        var status = _sut.Statuses().Single(s => s.Config.Name == "alpha");
        status.State.Should().Be(McpServerState.Failed);
        status.Error.Should().Be("npx not found");
    }

    [Fact]
    public async Task ToolsAsync_FailedServer_IsRetriedOnlyAfterAWhile()
    {
        // Arrange
        var attempts = 0;
        _connector.ConnectAsync(Arg.Is<McpServerConfig>(c => c.Name == "alpha"), Arg.Any<CancellationToken>()).Returns<IMcpConnection>(_ =>
        {
            attempts++;
            throw new McpConnectException("down");
        });
        await _sut.ToolsAsync(["alpha"], TestContext.Current.CancellationToken);

        // Act
        await _sut.ToolsAsync(["alpha"], TestContext.Current.CancellationToken);
        var beforeRetry = attempts;
        _time.Advance(TimeSpan.FromSeconds(31));
        await _sut.ToolsAsync(["alpha"], TestContext.Current.CancellationToken);

        // Assert
        beforeRetry.Should().Be(1);
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task ToolsAsync_UnsetVariable_FailsWithoutStartingTheServer()
    {
        // Arrange
        await _file.SaveAsync("""{"mcpServers": {"alpha": {"command": "a", "env": {"KEY": "${ALPHA_KEY}"}}}}""", TestContext.Current.CancellationToken);

        // Act
        var tools = await _sut.ToolsAsync(["alpha"], TestContext.Current.CancellationToken);

        // Assert
        tools.Should().BeEmpty();
        _sut.Statuses().Single().Error.Should().Contain("ALPHA_KEY");
        await _connector.DidNotReceive().ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ToolsAsync_Variables_AreExpandedForTheConnectorButNotInTheFile()
    {
        // Arrange
        await _file.SaveAsync("""{"mcpServers": {"alpha": {"command": "a", "env": {"KEY": "${ALPHA_KEY}"}}}}""", TestContext.Current.CancellationToken);
        _environment["ALPHA_KEY"] = "s3cret";
        McpServerConfig? seen = null;
        _connector.ConnectAsync(Arg.Do<McpServerConfig>(c => seen = c), Arg.Any<CancellationToken>()).Returns(_ => Connection());

        // Act
        await _sut.ToolsAsync(["alpha"], TestContext.Current.CancellationToken);

        // Assert
        seen!.Env["KEY"].Should().Be("s3cret");
        _file.Text.Should().Contain("${ALPHA_KEY}").And.NotContain("s3cret");
        _sut.Statuses().Single().Config.Env["KEY"].Should().Be("${ALPHA_KEY}");
    }

    [Fact]
    public async Task ToolsAsync_ServerNeverAnswers_FailsAfterTheTimeout()
    {
        // Arrange
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _connector.ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>()).Returns(call => NeverAnswers(started, call.Arg<CancellationToken>()));

        // Act
        var pending = _sut.ToolsAsync(["alpha"], TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(11));
        var tools = await pending;

        // Assert
        tools.Should().BeEmpty();
        _sut.Statuses().Single(s => s.Config.Name == "alpha").Error.Should().Contain("10 seconds");
    }

    [Fact]
    public async Task ToolsAsync_Cancelled_Throws_AndLeavesTheServerStopped()
    {
        // Arrange
        _connector.ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>()).Returns(call => CancelsWhileConnecting(call.Arg<CancellationToken>()));

        // Act
        var act = () => _sut.ToolsAsync(["alpha"], _cancel.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        _sut.Statuses().Single(s => s.Config.Name == "alpha").State.Should().Be(McpServerState.Stopped);
    }

    [Fact]
    public async Task ToolsAsync_ListingToolsFails_ClosesTheConnectionAndFails()
    {
        // Arrange
        var alpha = Connection();
        alpha.ListToolsAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<McpToolInfo>>(_ => throw new InvalidOperationException("bad reply"));
        _connector.ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>()).Returns(alpha);

        // Act
        await _sut.ToolsAsync(["alpha"], TestContext.Current.CancellationToken);

        // Assert
        _sut.Statuses().Single(s => s.Config.Name == "alpha").Error.Should().Be("bad reply");
        await alpha.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ReconnectAsync_StopsTheOldConnectionAndStartsANewOne()
    {
        // Arrange
        var first = Connection(Tool("one"));
        var second = Connection(Tool("two"));
        _connector.ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>()).Returns(first, second);
        await _sut.ToolsAsync(["alpha"], TestContext.Current.CancellationToken);

        // Act
        var status = await _sut.ReconnectAsync("alpha", TestContext.Current.CancellationToken);

        // Assert
        status!.State.Should().Be(McpServerState.Connected);
        status.Tools.Select(t => t.Name).Should().Equal("two");
        await first.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ReconnectAsync_UnknownServer_ReturnsNull()
    {
        // Act
        var status = await _sut.ReconnectAsync("nope", TestContext.Current.CancellationToken);

        // Assert
        status.Should().BeNull();
    }

    [Fact]
    public async Task ReconnectAsync_ServerThatIsTurnedOff_StillConnects()
    {
        // Arrange
        await _file.SetDisabledAsync("alpha", disabled: true, TestContext.Current.CancellationToken);
        _connector.ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>()).Returns(_ => Connection(Tool("one")));

        // Act
        var status = await _sut.ReconnectAsync("alpha", TestContext.Current.CancellationToken);

        // Assert
        status!.State.Should().Be(McpServerState.Connected);
    }

    [Fact]
    public async Task ConfigChange_StopsOnlyTheServerThatChanged()
    {
        // Arrange
        var alpha = Connection(Tool("a"));
        var beta = Connection(Tool("b"));
        _connector.ConnectAsync(Arg.Is<McpServerConfig>(c => c.Name == "alpha"), Arg.Any<CancellationToken>()).Returns(alpha);
        _connector.ConnectAsync(Arg.Is<McpServerConfig>(c => c.Name == "beta"), Arg.Any<CancellationToken>()).Returns(beta);
        await _sut.ToolsAsync(["alpha", "beta"], TestContext.Current.CancellationToken);

        // Act
        await _file.SaveAsync("""{"mcpServers": {"alpha": {"command": "a"}, "beta": {"command": "b", "args": ["--new"]}}}""", TestContext.Current.CancellationToken);
        await _sut.ReconcileAsync();

        // Assert
        await beta.Received(1).DisposeAsync();
        await alpha.DidNotReceive().DisposeAsync();
        _sut.Statuses().Single(s => s.Config.Name == "alpha").State.Should().Be(McpServerState.Connected);
        _sut.Statuses().Single(s => s.Config.Name == "beta").State.Should().Be(McpServerState.Stopped);
    }

    [Fact]
    public async Task ConfigChange_TrustedServerChanged_WithdrawsTrustOnlyForThatServer()
    {
        // Arrange
        await _permissions.SetMcpServerTrustedAsync("alpha", trusted: true, TestContext.Current.CancellationToken);
        await _permissions.SetMcpServerTrustedAsync("beta", trusted: true, TestContext.Current.CancellationToken);
        _ = _sut.Statuses();

        // Act
        await _file.SaveAsync("""{"mcpServers": {"alpha": {"command": "a"}, "beta": {"command": "other"}}}""", TestContext.Current.CancellationToken);
        await _sut.ReconcileAsync();

        // Assert
        _permissions.TrustedMcpServers.Should().Equal("alpha");
    }

    [Fact]
    public async Task ConfigChange_OnlyTheDisabledFlag_KeepsTrust()
    {
        // Arrange
        await _permissions.SetMcpServerTrustedAsync("alpha", trusted: true, TestContext.Current.CancellationToken);
        _ = _sut.Statuses();

        // Act
        await _file.SaveAsync("""{"mcpServers": {"alpha": {"command": "a", "disabled": true}, "beta": {"command": "b"}}}""", TestContext.Current.CancellationToken);
        await _sut.ReconcileAsync();

        // Assert
        _permissions.TrustedMcpServers.Should().Equal("alpha");
    }

    [Fact]
    public async Task ConfigChange_ServerRemoved_StopsIt()
    {
        // Arrange
        var alpha = Connection(Tool("a"));
        _connector.ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>()).Returns(alpha);
        await _sut.ToolsAsync(["alpha"], TestContext.Current.CancellationToken);

        // Act
        await _file.SaveAsync("""{"mcpServers": {"beta": {"command": "b"}}}""", TestContext.Current.CancellationToken);
        await _sut.ReconcileAsync();

        // Assert
        await alpha.Received(1).DisposeAsync();
        _sut.Statuses().Select(s => s.Config.Name).Should().Equal("beta");
    }

    [Fact]
    public async Task ConfigChange_ServerRemovedWhileConnecting_ClosesTheLateConnection()
    {
        // Arrange
        var late = Connection(Tool("a"));
        var connecting = new TaskCompletionSource<IMcpConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
        _connector.ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>()).Returns(connecting.Task);
        var pending = _sut.ToolsAsync(["alpha"], TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => _sut.Statuses().Single(s => s.Config.Name == "alpha").State == McpServerState.Connecting);
        await _file.SaveAsync("""{"mcpServers": {"beta": {"command": "b"}}}""", TestContext.Current.CancellationToken);
        var reconciling = _sut.ReconcileAsync();

        // Act
        connecting.SetResult(late);
        var tools = await pending.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await reconciling.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // Assert
        tools.Should().BeEmpty();
        await late.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_WhileAServerIsConnecting_ClosesTheLateConnection()
    {
        // Arrange
        var late = Connection(Tool("a"));
        var connecting = new TaskCompletionSource<IMcpConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
        _connector.ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>()).Returns(connecting.Task);
        var pending = _sut.ToolsAsync(["alpha"], TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => _sut.Statuses().Single(s => s.Config.Name == "alpha").State == McpServerState.Connecting);
        await _sut.DisposeAsync();

        // Act
        connecting.SetResult(late);
        var tools = await pending.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // Assert
        tools.Should().BeEmpty();
        await late.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ConfigChange_OnlyTheDisabledFlag_KeepsTheConnection()
    {
        // Arrange
        var alpha = Connection(Tool("a"));
        _connector.ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>()).Returns(alpha);
        await _sut.ToolsAsync(["alpha"], TestContext.Current.CancellationToken);

        // Act
        await _file.SetDisabledAsync("alpha", disabled: true, TestContext.Current.CancellationToken);
        await _sut.ReconcileAsync();

        // Assert
        await alpha.DidNotReceive().DisposeAsync();
    }

    [Fact]
    public async Task ConnectionClosedByTheServer_MarksItFailedSoItStartsAgain()
    {
        // Arrange
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var alpha = Connection(closed.Task, Tool("a"));
        var again = Connection(Tool("a"));
        _connector.ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>()).Returns(alpha, again);
        await _sut.ToolsAsync(["alpha"], TestContext.Current.CancellationToken);
        _sut.Changed += (_, _) =>
        {
            if (_sut.Statuses().Single(s => s.Config.Name == "alpha").State == McpServerState.Failed)
            {
                changed.TrySetResult();
            }
        };

        // Act
        closed.SetResult();
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(31));
        await _sut.ToolsAsync(["alpha"], TestContext.Current.CancellationToken);

        // Assert
        _sut.Statuses().Single(s => s.Config.Name == "alpha").State.Should().Be(McpServerState.Connected);
        await _connector.Received(2).ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Statuses_ListsEveryServerInFileOrder_WithThePromptCostOfConnectedOnes()
    {
        // Arrange
        _connector.ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>()).Returns(_ => Connection(Tool("read")));
        await _sut.ToolsAsync(["beta"], TestContext.Current.CancellationToken);

        // Act
        var statuses = _sut.Statuses();

        // Assert
        statuses.Select(s => s.Config.Name).Should().Equal("alpha", "beta");
        statuses[0].PromptCharacters.Should().Be(0);
        statuses[1].PromptCharacters.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DisposeAsync_ClosesEveryConnection()
    {
        // Arrange
        var alpha = Connection(Tool("a"));
        _connector.ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>()).Returns(alpha);
        await _sut.ToolsAsync(["alpha"], TestContext.Current.CancellationToken);

        // Act
        await _sut.DisposeAsync();

        // Assert
        await alpha.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ToolsAsync_CallThroughATool_ReachesTheConnectionWithTheServersOwnName()
    {
        // Arrange
        var alpha = Connection(Tool("read"));
        alpha.CallToolAsync("read", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ToolResult.Ok("done"));
        _connector.ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>()).Returns(alpha);
        var tools = await _sut.ToolsAsync(["alpha"], TestContext.Current.CancellationToken);

        // Act
        var result = await tools.Single().ExecuteAsync(ToolArguments.Parse("{}"), new ToolContext(_dir), TestContext.Current.CancellationToken);

        // Assert
        result.Output.Should().Be("done");
    }

    [Theory]
    [InlineData(false, null, true)]
    [InlineData(true, null, false)]
    [InlineData(false, false, false)]
    [InlineData(true, true, true)]
    public void IsEnabled_ChatChoiceWinsOverTheGlobalState(bool disabled, bool? chosen, bool expected)
    {
        // Arrange
        var server = new McpServerConfig("s", McpTransport.Stdio, "x", [], new Dictionary<string, string>(), null, null, new Dictionary<string, string>(), disabled);
        IReadOnlyDictionary<string, bool>? overrides = chosen is { } value ? new Dictionary<string, bool> { ["s"] = value } : null;

        // Act
        var enabled = McpManager.IsEnabled(server, overrides);

        // Assert
        enabled.Should().Be(expected);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        // A state change is raised from the manager's own work, so yield to it instead of sleeping.
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        bound.CancelAfter(TimeSpan.FromSeconds(30));
        while (!condition())
        {
            bound.Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }

    private async Task<IMcpConnection> CancelsWhileConnecting(CancellationToken ct)
    {
        await _cancel.CancelAsync();
        ct.ThrowIfCancellationRequested();
        return null!;
    }

    private static async Task<IMcpConnection> NeverAnswers(TaskCompletionSource started, CancellationToken ct)
    {
        started.SetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        return null!;
    }

    private static McpToolInfo Tool(string name) => new(name, "Does " + name, """{"type":"object","properties":{}}""");

    private static IMcpConnection Connection(params McpToolInfo[] tools) => Connection(new TaskCompletionSource().Task, tools);

    private static IMcpConnection Connection(Task closed, params McpToolInfo[] tools)
    {
        var connection = Substitute.For<IMcpConnection>();
        connection.Closed.Returns(closed);
        connection.ListToolsAsync(Arg.Any<CancellationToken>()).Returns(tools);
        return connection;
    }
}
