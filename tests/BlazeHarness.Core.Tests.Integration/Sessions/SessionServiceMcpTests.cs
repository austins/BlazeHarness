using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Config;
using BlazeHarness.Core.Mcp;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Providers;
using BlazeHarness.Core.Sessions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable IDISP001 // The service disposes the sessions it opens.

namespace BlazeHarness.Core.Tests.Integration.Sessions;

public sealed class SessionServiceMcpTests : IAsyncLifetime
{
    private readonly TempFolder _temp = new("bh-svc-mcp-");
    private readonly string _work;
    private readonly IMcpConnector _connector = Substitute.For<IMcpConnector>();
    private readonly SessionStore _store;
    private readonly McpConfigFile _file;
    private readonly McpManager _manager;
    private readonly JsonFileStore<AppConfig> _config;
    private readonly PermissionDefaults _defaults;
    private readonly ScriptedChatClient _client = new([ScriptedChatClient.Text("ok")]);
    private readonly SessionService _sut;

    public SessionServiceMcpTests()
    {
        _work = Path.Combine(_temp.Root, "work");
        Directory.CreateDirectory(_work);
        var paths = new AppPaths(Path.Combine(_temp.Root, "app"), _temp.Root);
        _file = new McpConfigFile(Path.Combine(_temp.Root, "mcp.json"), new LoggerFake<McpConfigFile>(), watch: false);
        _manager = new McpManager(_file, _connector, new LoggerFake<McpManager>());
        _config = new JsonFileStore<AppConfig>(Path.Combine(_temp.Root, "config.json"), new LoggerFake<SessionServiceMcpTests>(), watch: false);
        _defaults = new PermissionDefaults(_config, paths);
        _store = new SessionStore(paths, NullLoggerFactory.Instance);
        _sut = new SessionService(_store, (_, _) => Task.FromResult<IChatClient>(_client), NullLoggerFactory.Instance, defaults: _defaults, mcp: _manager);
    }

    public async ValueTask InitializeAsync()
    {
        await _file.SaveAsync("""{"mcpServers": {"files": {"command": "f"}, "off": {"command": "o", "disabled": true}}}""", TestContext.Current.CancellationToken);
        var connection = Substitute.For<IMcpConnection>();
        connection.Closed.Returns(new TaskCompletionSource().Task);
        connection.ListToolsAsync(Arg.Any<CancellationToken>()).Returns([new McpToolInfo("read", "Reads.", """{"type":"object","properties":{}}""")]);
        _connector.ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>()).Returns(connection);
    }

    public async ValueTask DisposeAsync()
    {
        _sut.Dispose();
        await _manager.DisposeAsync();
        _file.Dispose();
        _config.Dispose();
        _store.Dispose();
        _client.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task SendAsync_ServerOnByDefault_OffersItsToolsAndLeavesTheDisabledOneAlone()
    {
        // Arrange
        var info = await _sut.CreateAsync(_work, TestContext.Current.CancellationToken);

        // Act
        await _sut.SendAsync(info.Id, "hi", Target());

        // Assert
        _client.Options[0]!.Tools!.Select(t => t.Name).Should().Contain("files__read").And.NotContain(name => name.StartsWith("off__", StringComparison.Ordinal));
        await _connector.Received(1).ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_ServerTurnedOffForTheSession_DoesNotOfferOrStartIt()
    {
        // Arrange
        var info = await _sut.CreateAsync(_work, TestContext.Current.CancellationToken);
        await _sut.SetMcpServerAsync(info.Id, "files", enabled: false);

        // Act
        await _sut.SendAsync(info.Id, "hi", Target());

        // Assert
        _client.Options[0]!.Tools!.Select(t => t.Name).Should().NotContain("files__read");
        await _connector.DidNotReceive().ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_ServerThatIsOffGloballyButOnForTheSession_OffersIt()
    {
        // Arrange
        var info = await _sut.CreateAsync(_work, TestContext.Current.CancellationToken);
        await _sut.SetMcpServerAsync(info.Id, "off", enabled: true);

        // Act
        await _sut.SendAsync(info.Id, "hi", Target());

        // Assert
        _client.Options[0]!.Tools!.Select(t => t.Name).Should().Contain("off__read");
    }

    [Fact]
    public async Task SetMcpServerAsync_ChoiceThenNull_SavesAndClearsIt()
    {
        // Arrange
        var info = await _sut.CreateAsync(_work, TestContext.Current.CancellationToken);
        var files = _manager.Statuses().Single(s => s.Config.Name == "files").Config;

        // Act
        await _sut.SetMcpServerAsync(info.Id, "files", enabled: false);
        var chosen = _store.Find(info.Id)!.McpServers;
        var effective = _sut.IsMcpServerEnabled(info.Id, files);
        await _sut.SetMcpServerAsync(info.Id, "files", enabled: null);

        // Assert
        chosen.Should().Equal(new Dictionary<string, bool> { ["files"] = false });
        effective.Should().BeFalse();
        _store.Find(info.Id)!.McpServers.Should().BeNull();
        _sut.IsMcpServerEnabled(info.Id, files).Should().BeTrue();
    }

    [Fact]
    public async Task OpenAsync_ServerTheUserTrusts_IsTrustedByTheSessionsPermissions()
    {
        // Arrange
        var info = await _sut.CreateAsync(_work, TestContext.Current.CancellationToken);
        var session = await _sut.OpenAsync(info.Id);
        await _defaults.SetMcpServerTrustedAsync("files", trusted: true, TestContext.Current.CancellationToken);

        // Assert
        session!.Permissions.TrustedMcpServer("files").Should().BeTrue();
        session.Permissions.TrustedMcpServer("off").Should().BeFalse();
    }

    private static ChatTarget Target()
    {
        var model = new ModelConfig { Id = "m", ContextWindow = 8000, MaxOutputTokens = 1000 };
        var provider = new ProviderConfig { Id = "p", DisplayName = "P", BaseUrl = "http://localhost/v1", Models = [model] };
        return new ChatTarget(provider, model, null);
    }
}
