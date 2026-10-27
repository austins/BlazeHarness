using BlazeHarness.Core.Mcp;
using BlazeHarness.Core.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazeHarness.Core.Tests.Integration.Mcp;

public sealed class McpServerEditorTests : IAsyncDisposable
{
    private readonly TempFolder _temp = new("bh-mcped-");
    private readonly McpConfigFile _file;
    private readonly FileSecretStore _secrets;
    private readonly McpManager _manager;
    private readonly McpServerEditor _sut;

    public McpServerEditorTests()
    {
        Directory.CreateDirectory(_temp.Root);
        _file = new McpConfigFile(Path.Combine(_temp.Root, "mcp.json"), NullLogger<McpConfigFile>.Instance, watch: false);
        _secrets = new FileSecretStore(Path.Combine(_temp.Root, "secrets.json"));
        _manager = new McpManager(_file, Substitute.For<IMcpConnector>(), NullLogger<McpManager>.Instance, secrets: _secrets);
        _sut = new McpServerEditor(_file, _secrets, _manager);
    }

    public async ValueTask DisposeAsync()
    {
        await _manager.DisposeAsync();
        _file.Dispose();
        _secrets.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task SaveAsync_NewLocalServer_WritesItToTheFile()
    {
        // Arrange
        var draft = LocalDraft("files");
        draft.ArgsText = "-y\n  pkg  \n\n--flag";
        draft.Env.Add(new McpDraftEntry { Name = "MODE", Value = "fast" });

        // Act
        var problems = await _sut.SaveAsync(draft, TestContext.Current.CancellationToken);

        // Assert
        problems.Should().BeEmpty();
        var server = _file.Parsed.Servers.Should().ContainSingle().Subject;
        server.Command.Should().Be("npx");
        server.Args.Should().Equal("-y", "pkg", "--flag");
        server.Env["MODE"].Should().Be("fast");
    }

    [Fact]
    public async Task SaveAsync_SecretEntry_GoesToTheStoreAndTheFileHoldsAReference()
    {
        // Arrange
        var draft = LocalDraft("files");
        draft.Env.Add(new McpDraftEntry { Name = "TOKEN", Value = "s3cret", Secret = true });

        // Act
        var problems = await _sut.SaveAsync(draft, TestContext.Current.CancellationToken);

        // Assert
        problems.Should().BeEmpty();
        _file.Text.Should().Contain("${MCP_FILES_TOKEN}").And.NotContain("s3cret");
        (await _secrets.GetAsync("mcp:MCP_FILES_TOKEN", TestContext.Current.CancellationToken)).Should().Be("s3cret");
    }

    [Fact]
    public async Task OpenAsync_SavedSecret_IsMarkedStoredWithoutItsValue()
    {
        // Arrange
        var draft = LocalDraft("files");
        draft.Env.Add(new McpDraftEntry { Name = "TOKEN", Value = "s3cret", Secret = true });
        await _sut.SaveAsync(draft, TestContext.Current.CancellationToken);

        // Act
        var opened = await _sut.OpenAsync(_file.Parsed.Servers.Single(), TestContext.Current.CancellationToken);

        // Assert
        var entry = opened.Env.Should().ContainSingle().Subject;
        entry.Secret.Should().BeTrue();
        entry.HasStoredSecret.Should().BeTrue();
        entry.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_EditWithoutRetypingASecret_KeepsTheStoredValue()
    {
        // Arrange
        var draft = LocalDraft("files");
        draft.Env.Add(new McpDraftEntry { Name = "TOKEN", Value = "s3cret", Secret = true });
        await _sut.SaveAsync(draft, TestContext.Current.CancellationToken);
        var edit = await _sut.OpenAsync(_file.Parsed.Servers.Single(), TestContext.Current.CancellationToken);
        edit.Command = "other";

        // Act
        var problems = await _sut.SaveAsync(edit, TestContext.Current.CancellationToken);

        // Assert
        problems.Should().BeEmpty();
        _file.Parsed.Servers.Single().Command.Should().Be("other");
        (await _secrets.GetAsync("mcp:MCP_FILES_TOKEN", TestContext.Current.CancellationToken)).Should().Be("s3cret");
    }

    [Fact]
    public async Task SaveAsync_SecretEntryTurnedIntoAPlainOne_DeletesTheStoredSecret()
    {
        // Arrange
        var draft = LocalDraft("files");
        draft.Env.Add(new McpDraftEntry { Name = "TOKEN", Value = "s3cret", Secret = true });
        await _sut.SaveAsync(draft, TestContext.Current.CancellationToken);
        var edit = await _sut.OpenAsync(_file.Parsed.Servers.Single(), TestContext.Current.CancellationToken);
        edit.Env[0].Secret = false;
        edit.Env[0].Value = "plain";

        // Act
        await _sut.SaveAsync(edit, TestContext.Current.CancellationToken);

        // Assert
        (await _secrets.GetAsync("mcp:MCP_FILES_TOKEN", TestContext.Current.CancellationToken)).Should().BeNull();
        _file.Parsed.Servers.Single().Env["TOKEN"].Should().Be("plain");
    }

    [Fact]
    public async Task SaveAsync_RemoteServer_WritesTheUrlAndHeaders()
    {
        // Arrange
        var draft = new McpServerDraft { IsNew = true, Name = "web", Transport = McpTransport.Http, Url = "https://x.test/mcp" };
        draft.Headers.Add(new McpDraftEntry { Name = "Authorization", Value = "Bearer abc", Secret = true });

        // Act
        var problems = await _sut.SaveAsync(draft, TestContext.Current.CancellationToken);

        // Assert
        problems.Should().BeEmpty();
        var server = _file.Parsed.Servers.Single();
        server.Url.Should().Be("https://x.test/mcp");
        server.Headers["Authorization"].Should().Be("${MCP_WEB_AUTHORIZATION}");
        (await _secrets.GetAsync("mcp:MCP_WEB_AUTHORIZATION", TestContext.Current.CancellationToken)).Should().Be("Bearer abc");
    }

    [Fact]
    public async Task SaveAsync_NewServerWithAnExistingName_ReportsAProblemAndKeepsTheOne()
    {
        // Arrange
        await _sut.SaveAsync(LocalDraft("files"), TestContext.Current.CancellationToken);
        var duplicate = LocalDraft("files");
        duplicate.Command = "other";

        // Act
        var problems = await _sut.SaveAsync(duplicate, TestContext.Current.CancellationToken);

        // Assert
        problems.Should().ContainSingle().Which.Should().Contain("already");
        _file.Parsed.Servers.Single().Command.Should().Be("npx");
    }

    [Theory]
    [InlineData("", "npx")]
    [InlineData("files", "")]
    public async Task SaveAsync_MissingNameOrCommand_ReportsAProblem(string name, string command)
    {
        // Arrange
        var draft = LocalDraft(name);
        draft.Command = command;

        // Act
        var problems = await _sut.SaveAsync(draft, TestContext.Current.CancellationToken);

        // Assert
        problems.Should().NotBeEmpty();
        _file.Parsed.Servers.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_SecretWithoutAValue_ReportsAProblem()
    {
        // Arrange
        var draft = LocalDraft("files");
        draft.Env.Add(new McpDraftEntry { Name = "TOKEN", Secret = true });

        // Act
        var problems = await _sut.SaveAsync(draft, TestContext.Current.CancellationToken);

        // Assert
        problems.Should().NotBeEmpty();
        _file.Parsed.Servers.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_TwoEntriesThatShareAVariable_ReportsAProblem()
    {
        // Arrange
        var draft = LocalDraft("files");
        draft.Env.Add(new McpDraftEntry { Name = "A-B", Value = "1", Secret = true });
        draft.Env.Add(new McpDraftEntry { Name = "A_B", Value = "2", Secret = true });

        // Act
        var problems = await _sut.SaveAsync(draft, TestContext.Current.CancellationToken);

        // Assert
        problems.Should().NotBeEmpty();
        _file.Parsed.Servers.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveAsync_Server_TakesItOutAndDeletesItsSecrets()
    {
        // Arrange
        var draft = LocalDraft("files");
        draft.Env.Add(new McpDraftEntry { Name = "TOKEN", Value = "s3cret", Secret = true });
        await _sut.SaveAsync(draft, TestContext.Current.CancellationToken);

        // Act
        var removed = await _sut.RemoveAsync("files", TestContext.Current.CancellationToken);

        // Assert
        removed.Should().BeTrue();
        _file.Parsed.Servers.Should().BeEmpty();
        (await _secrets.GetAsync("mcp:MCP_FILES_TOKEN", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_UnknownServer_ReturnsFalse()
    {
        // Act
        var removed = await _sut.RemoveAsync("nope", TestContext.Current.CancellationToken);

        // Assert
        removed.Should().BeFalse();
    }

    private static McpServerDraft LocalDraft(string name) => new() { IsNew = true, Name = name, Transport = McpTransport.Stdio, Command = "npx" };
}
