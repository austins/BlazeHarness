using BlazeHarness.Core.Mcp;

namespace BlazeHarness.Core.Tests.Integration.Mcp;

public sealed class McpConfigFileTests : IDisposable
{
    private const string OneServer = """{"mcpServers": {"files": {"command": "run"}}}""";

    private readonly TempFolder _temp = new("bh-mcp-");
    private readonly string _path;

    public McpConfigFileTests()
    {
        Directory.CreateDirectory(_temp.Root);
        _path = Path.Combine(_temp.Root, "mcp.json");
    }

    public void Dispose()
    {
        _temp.Dispose();
    }

    [Fact]
    public void Constructor_NoFile_IsEmpty()
    {
        // Act
        using var sut = new McpConfigFile(_path, new LoggerFake<McpConfigFileTests>(), watch: false);

        // Assert
        sut.Text.Should().BeEmpty();
        sut.Parsed.Servers.Should().BeEmpty();
        sut.Parsed.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Constructor_ExistingFile_ParsesIt()
    {
        // Arrange
        await File.WriteAllTextAsync(_path, OneServer, TestContext.Current.CancellationToken);

        // Act
        using var sut = new McpConfigFile(_path, new LoggerFake<McpConfigFileTests>(), watch: false);

        // Assert
        sut.Parsed.Servers.Should().ContainSingle().Which.Name.Should().Be("files");
    }

    [Fact]
    public async Task OnExternalChange_EditedOnDisk_ReloadsAndRaisesChanged()
    {
        // Arrange
        using var sut = new McpConfigFile(_path, new LoggerFake<McpConfigFileTests>(), watch: false);
        var raised = 0;
        sut.Changed += (_, _) => raised++;
        await File.WriteAllTextAsync(_path, OneServer, TestContext.Current.CancellationToken);

        // Act
        sut.OnExternalChange();

        // Assert
        raised.Should().Be(1);
        sut.Parsed.Servers.Should().ContainSingle();
    }

    [Fact]
#pragma warning disable IDISP016, IDISP017 // Using the file after disposal is the point of the test.
    public void OnExternalChange_AfterDispose_DoesNotThrow()
    {
        // Arrange
        var sut = new McpConfigFile(_path, new LoggerFake<McpConfigFileTests>(), watch: false);
        sut.Dispose();

        // Act
        var act = sut.OnExternalChange;

        // Assert
        act.Should().NotThrow();
    }
#pragma warning restore IDISP016, IDISP017

    [Fact]
    public async Task SaveAsync_ValidText_WritesTheTextAsGiven()
    {
        // Arrange
        using var sut = new McpConfigFile(_path, new LoggerFake<McpConfigFileTests>(), watch: false);
        var raised = 0;
        sut.Changed += (_, _) => raised++;

        // Act
        var result = await sut.SaveAsync(OneServer, TestContext.Current.CancellationToken);

        // Assert
        result.Errors.Should().BeEmpty();
        (await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken)).Should().Be(OneServer);
        sut.Text.Should().Be(OneServer);
        sut.Parsed.Servers.Should().HaveCount(1);
        raised.Should().Be(1);
        Directory.GetFiles(_temp.Root).Select(Path.GetFileName).Should().BeEquivalentTo("mcp.json");
    }

    [Fact]
    public async Task SaveAsync_TextWithErrors_LeavesTheFileAlone()
    {
        // Arrange
        await File.WriteAllTextAsync(_path, OneServer, TestContext.Current.CancellationToken);
        using var sut = new McpConfigFile(_path, new LoggerFake<McpConfigFileTests>(), watch: false);

        // Act
        var result = await sut.SaveAsync("{ nope", TestContext.Current.CancellationToken);

        // Assert
        result.Errors.Should().NotBeEmpty();
        (await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken)).Should().Be(OneServer);
        sut.Text.Should().Be(OneServer);
    }

    [Fact]
    public async Task SetDisabledAsync_KnownServer_WritesTheFlag()
    {
        // Arrange
        await File.WriteAllTextAsync(_path, OneServer, TestContext.Current.CancellationToken);
        using var sut = new McpConfigFile(_path, new LoggerFake<McpConfigFileTests>(), watch: false);

        // Act
        var saved = await sut.SetDisabledAsync("files", disabled: true, TestContext.Current.CancellationToken);

        // Assert
        saved.Should().BeTrue();
        sut.Parsed.Servers.Should().ContainSingle().Which.Disabled.Should().BeTrue();
    }

    [Fact]
    public async Task SetDisabledAsync_UnknownServer_ReturnsFalse()
    {
        // Arrange
        await File.WriteAllTextAsync(_path, OneServer, TestContext.Current.CancellationToken);
        using var sut = new McpConfigFile(_path, new LoggerFake<McpConfigFileTests>(), watch: false);

        // Act
        var saved = await sut.SetDisabledAsync("other", disabled: true, TestContext.Current.CancellationToken);

        // Assert
        saved.Should().BeFalse();
        sut.Text.Should().Be(OneServer);
    }

    [Fact]
    public async Task ExternalEdit_IsPickedUpAndRaisesChanged()
    {
        // Arrange
        using var sut = new McpConfigFile(_path, new LoggerFake<McpConfigFileTests>());
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.Changed += (_, _) => changed.TrySetResult();

        // Act
        await File.WriteAllTextAsync(_path, OneServer, TestContext.Current.CancellationToken);
        await changed.Task.WaitAsync(TestTimeouts.Default, TestContext.Current.CancellationToken);

        // Assert
        sut.Parsed.Servers.Should().ContainSingle().Which.Name.Should().Be("files");
    }

    [Fact]
    public async Task ExternalEdit_WithErrors_IsKeptSoTheUserSeesThem()
    {
        // Arrange
        using var sut = new McpConfigFile(_path, new LoggerFake<McpConfigFileTests>());
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.Changed += (_, _) => changed.TrySetResult();

        // Act
        await File.WriteAllTextAsync(_path, "{ nope", TestContext.Current.CancellationToken);
        await changed.Task.WaitAsync(TestTimeouts.Default, TestContext.Current.CancellationToken);

        // Assert
        sut.Parsed.Errors.Should().NotBeEmpty();
        sut.Text.Should().Be("{ nope");
    }
}
