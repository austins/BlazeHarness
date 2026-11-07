using BlazeHarness.Core.Mcp;
using BlazeHarness.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazeHarness.Core.Tests.Integration.Mcp;

public sealed class SdkMcpConnectorTests : IDisposable
{
    private const string EchoServer = """
        import sys, json
        for line in sys.stdin:
            m = json.loads(line)
            if 'id' not in m:
                continue
            method = m['method']
            if method == 'initialize':
                result = {'protocolVersion': m['params']['protocolVersion'], 'capabilities': {'tools': {}}, 'serverInfo': {'name': 'py', 'version': '1'}}
            elif method == 'tools/list':
                result = {'tools': [{'name': 'echo', 'description': 'Echoes text.', 'inputSchema': {'type': 'object', 'properties': {'text': {'type': 'string'}}}}]}
            elif method == 'tools/call':
                result = {'content': [{'type': 'text', 'text': 'echo:' + m['params']['arguments']['text']}]}
            else:
                sys.stdout.write(json.dumps({'jsonrpc': '2.0', 'id': m['id'], 'error': {'code': -32601, 'message': 'Method not found'}}) + '\n')
                sys.stdout.flush()
                continue
            sys.stdout.write(json.dumps({'jsonrpc': '2.0', 'id': m['id'], 'result': result}) + '\n')
            sys.stdout.flush()
        """;

    private const string PythonSkip = "Needs Python on PATH to act as a stdio MCP server.";
    private const string FailingServer = "import os, sys; sys.stderr.write('bad key ' + os.environ['API_KEY']); sys.exit(1)";

    private readonly TempFolder _temp = new("bh-mcp-stdio-");
    private readonly SdkMcpConnector _sut = new(NullLoggerFactory.Instance);

    public void Dispose() => _temp.Dispose();

    [Fact(Skip = PythonSkip, SkipUnless = nameof(TestPython.IsAvailable), SkipType = typeof(TestPython))]
    public async Task ConnectAsync_StdioServer_ListsAndCallsItsTools()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var script = Path.Combine(_temp.Root, "server.py");
        await File.WriteAllTextAsync(script, EchoServer, ct);

        // Act
        await using var connection = await _sut.ConnectAsync(Config(["-u", script]), ct).Bounded();
        var tools = await connection.ListToolsAsync(ct).Bounded();
        var result = await connection.CallToolAsync("echo", """{"text":"hi"}""", ct).Bounded();

        // Assert
        tools.Should().ContainSingle().Which.Name.Should().Be("echo");
        result.IsError.Should().BeFalse();
        result.Output.Should().Be("echo:hi");
    }

    [Fact(Skip = PythonSkip, SkipUnless = nameof(TestPython.IsAvailable), SkipType = typeof(TestPython))]
    public async Task ConnectAsync_ServerExitsWritingASetting_FailsWithoutRevealingTheValue()
    {
        // Arrange
        var env = new Dictionary<string, string> { ["API_KEY"] = "topsecret-value" };

        // Act
        var act = async () => await _sut.ConnectAsync(Config(["-c", FailingServer], env), TestContext.Current.CancellationToken).Bounded();

        // Assert
        var thrown = await act.Should().ThrowAsync<McpConnectException>();
        // The stderr tail can arrive after the exit is noticed, so only the absence of the value is deterministic.
        thrown.Which.Message.Should().NotContain("topsecret-value");
    }

    [Fact]
    public async Task ConnectAsync_CommandDoesNotExist_ThrowsConnectException()
    {
        // Arrange
        var config = Config([]) with { Command = Path.Combine(_temp.Root, "no-such-server") };

        // Act
        var act = async () => await _sut.ConnectAsync(config, TestContext.Current.CancellationToken).Bounded();

        // Assert
        await act.Should().ThrowAsync<McpConnectException>();
    }

    private McpServerConfig Config(string[] args, Dictionary<string, string>? env = null)
    {
        return new McpServerConfig("py", McpTransport.Stdio, TestPython.Command!, args, env ?? [], _temp.Root, null, new Dictionary<string, string>(), false);
    }
}
