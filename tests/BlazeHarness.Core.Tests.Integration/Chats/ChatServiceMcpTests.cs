using BlazeHarness.Core.Config;
using BlazeHarness.Core.Mcp;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Tests.Shared;
using BlazeHarness.Tests.Shared.Agent;
using Microsoft.Extensions.Logging.Abstractions;
using ChatService = BlazeHarness.Core.Chats.ChatService;

#pragma warning disable IDISP001 // The service disposes the chats it opens.

namespace BlazeHarness.Core.Tests.Integration.Chats;

public sealed class ChatServiceMcpTests : IAsyncLifetime
{
    private readonly ChatServiceRig _rig = new("bh-svc-mcp-", "ok");
    private readonly IMcpConnector _connector = Substitute.For<IMcpConnector>();
    private readonly McpConfigFile _file;
    private readonly McpManager _manager;
    private readonly JsonFileStore<AppConfig> _config;
    private readonly PermissionDefaults _defaults;

    public ChatServiceMcpTests()
    {
        _file = new McpConfigFile(Path.Combine(_rig.Root, "mcp.json"), new LoggerFake<McpConfigFile>(), TimeProvider.System, watch: false);
        _manager = new McpManager(_file, _connector, new LoggerFake<McpManager>(), TimeProvider.System);
        _config = new JsonFileStore<AppConfig>(Path.Combine(_rig.Root, "config.json"), NullLogger<JsonFileStore<AppConfig>>.Instance, TimeProvider.System, watch: false);
        _defaults = new PermissionDefaults(_config, _rig.Paths);
        _rig.Build(defaults: _defaults, mcp: _manager);
    }

    private ChatService Sut => _rig.Service;

    public async ValueTask InitializeAsync()
    {
        await _file.SaveAsync("""{"mcpServers": {"files": {"command": "f", "alwaysLoad": true}, "off": {"command": "o", "disabled": true, "alwaysLoad": true}, "lazy": {"command": "l"}}}""", TestContext.Current.CancellationToken);
        var connection = Substitute.For<IMcpConnection>();
        connection.Closed.Returns(new TaskCompletionSource().Task);
        connection.ListToolsAsync(Arg.Any<CancellationToken>()).Returns([new McpToolInfo("read", "Reads.", """{"type":"object","properties":{}}""")]);
        _connector.ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>()).Returns(connection);
    }

    public async ValueTask DisposeAsync()
    {
        _rig.Dispose();
        await _manager.DisposeAsync();
        _file.Dispose();
        _config.Dispose();
    }

    [Fact]
    public async Task SendAsync_ServerOnByDefault_OffersItsToolsAndLeavesTheDisabledOneAlone()
    {
        // Arrange
        var info = await Sut.CreateAsync(_rig.Work, TestContext.Current.CancellationToken);

        // Act
        await Sut.SendAsync(info.Id, "hi", TestTargets.Create("p", "m"), cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _rig.Client.Options[0]!.Tools!.Select(t => t.Name).Should().Contain("files__read").And.NotContain(name => name.StartsWith("off__", StringComparison.Ordinal));
        await _connector.Received(1).ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_ServerWithoutAlwaysLoad_OffersTheSearchToolsAndDoesNotStartIt()
    {
        // Arrange
        var info = await Sut.CreateAsync(_rig.Work, TestContext.Current.CancellationToken);

        // Act
        await Sut.SendAsync(info.Id, "hi", TestTargets.Create("p", "m"), cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var names = _rig.Client.Options[0]!.Tools!.Select(t => t.Name).ToList();
        names.Should().Contain(["find_tools", "use_tool"]).And.NotContain("lazy__read");
        names[^1].Should().Be("use_tool");
        await _connector.Received(1).ConnectAsync(Arg.Is<McpServerConfig>(c => c.Name == "files"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_ServerTurnedOffForTheChat_DoesNotOfferOrStartIt()
    {
        // Arrange
        var info = await Sut.CreateAsync(_rig.Work, TestContext.Current.CancellationToken);
        await Sut.SetMcpServerAsync(info.Id, "files", enabled: false, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await Sut.SendAsync(info.Id, "hi", TestTargets.Create("p", "m"), cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _rig.Client.Options[0]!.Tools!.Select(t => t.Name).Should().NotContain("files__read");
        await _connector.DidNotReceive().ConnectAsync(Arg.Any<McpServerConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_ServerThatIsOffGloballyButOnForTheChat_OffersIt()
    {
        // Arrange
        var info = await Sut.CreateAsync(_rig.Work, TestContext.Current.CancellationToken);
        await Sut.SetMcpServerAsync(info.Id, "off", enabled: true, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await Sut.SendAsync(info.Id, "hi", TestTargets.Create("p", "m"), cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _rig.Client.Options[0]!.Tools!.Select(t => t.Name).Should().Contain("off__read");
    }

    [Fact]
    public async Task SetMcpServerAsync_ChoiceThenNull_SavesAndClearsIt()
    {
        // Arrange
        var info = await Sut.CreateAsync(_rig.Work, TestContext.Current.CancellationToken);
        var files = _manager.Statuses().Single(s => s.Config.Name == "files").Config;

        // Act
        await Sut.SetMcpServerAsync(info.Id, "files", enabled: false, cancellationToken: TestContext.Current.CancellationToken);
        var chosen = _rig.Store.Find(info.Id)!.McpServers;
        var effective = Sut.IsMcpServerEnabled(info.Id, files);
        await Sut.SetMcpServerAsync(info.Id, "files", enabled: null, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        chosen.Should().Equal(new Dictionary<string, bool> { ["files"] = false });
        effective.Should().BeFalse();
        _rig.Store.Find(info.Id)!.McpServers.Should().BeNull();
        Sut.IsMcpServerEnabled(info.Id, files).Should().BeTrue();
    }

    [Fact]
    public async Task OpenAsync_ServerTheUserTrusts_IsTrustedByTheChatsPermissions()
    {
        // Arrange
        var info = await Sut.CreateAsync(_rig.Work, TestContext.Current.CancellationToken);
        var chat = await Sut.OpenAsync(info.Id, cancellationToken: TestContext.Current.CancellationToken);
        await _defaults.SetMcpServerTrustedAsync("files", trusted: true, TestContext.Current.CancellationToken);

        // Assert
        chat!.Permissions.TrustedMcpServer("files").Should().BeTrue();
        chat.Permissions.TrustedMcpServer("off").Should().BeFalse();
    }
}
