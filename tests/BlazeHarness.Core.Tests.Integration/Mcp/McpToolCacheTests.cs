using BlazeHarness.Core.Mcp;
using BlazeHarness.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazeHarness.Core.Tests.Integration.Mcp;

public sealed class McpToolCacheTests : IDisposable
{
    private static readonly McpToolInfo[] Tools = [new("read", "Reads.", """{"type":"object","properties":{}}""")];

    private readonly TempFolder _temp = new("bh-mcpcache-");
    private readonly McpToolCache _sut;

    public McpToolCacheTests()
    {
        Directory.CreateDirectory(_temp.Root);
        _sut = Create();
    }

    public void Dispose()
    {
        _sut.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task Get_AfterSet_ReturnsTheToolsAndKeepsThemAcrossInstances()
    {
        // Arrange
        var server = Server("files", "a");
        await _sut.SetAsync(server, Tools, Names("files"));

        // Act
        using var reopened = Create();
        var tools = reopened.Get(server);

        // Assert
        tools.Should().Equal(Tools);
        File.Exists(Path.Combine(_temp.Root, "cache", "mcp-tools.json")).Should().BeTrue();
    }

    [Fact]
    public async Task Get_ServerWhoseSettingsChanged_ReturnsNull()
    {
        // Arrange
        await _sut.SetAsync(Server("files", "a"), Tools, Names("files"));

        // Act
        var tools = _sut.Get(Server("files", "b"));

        // Assert
        tools.Should().BeNull();
    }

    [Fact]
    public async Task Get_OnlyAlwaysLoadChanged_StillReturnsTheTools()
    {
        // Arrange
        await _sut.SetAsync(Server("files", "a"), Tools, Names("files"));
        var changed = Server("files", "a") with { AlwaysLoad = ["*"] };

        // Act
        var tools = _sut.Get(changed);

        // Assert
        tools.Should().Equal(Tools);
    }

    [Fact]
    public void Get_NeverCached_ReturnsNull()
    {
        // Act
        var tools = _sut.Get(Server("files", "a"));

        // Assert
        tools.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_ServerNoLongerConfigured_IsForgotten()
    {
        // Arrange
        await _sut.SetAsync(Server("old", "a"), Tools, Names("old"));

        // Act
        await _sut.SetAsync(Server("files", "a"), Tools, Names("files"));

        // Assert
        _sut.Get(Server("old", "a")).Should().BeNull();
        _sut.Get(Server("files", "a")).Should().Equal(Tools);
    }

    private static McpServerConfig Server(string name, string arg)
        => new(name, McpTransport.Stdio, "cmd", [arg], new Dictionary<string, string>(), null, null, new Dictionary<string, string>(), false);

    private static HashSet<string> Names(params string[] names) => [.. names];

    private McpToolCache Create() => new(new AppPaths(_temp.Root, _temp.Root), NullLoggerFactory.Instance, TimeProvider.System);
}
