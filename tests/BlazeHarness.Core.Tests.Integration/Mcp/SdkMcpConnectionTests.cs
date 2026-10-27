using BlazeHarness.Core.Mcp;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.IO.Pipelines;

namespace BlazeHarness.Core.Tests.Integration.Mcp;

public sealed class SdkMcpConnectionTests : IAsyncLifetime
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Pipe _toClient = new();
    private readonly Pipe _toServer = new();
    private McpServer _server = null!;
    private SdkMcpConnection _sut = null!;

    public async ValueTask InitializeAsync()
    {
        var options = new McpServerOptions
        {
            ToolCollection =
            [
                McpServerTool.Create(
                    Add,
                    new McpServerToolCreateOptions { Name = "add", Description = "Adds two numbers.", ReadOnly = true }),
                McpServerTool.Create(
                    () => "ignored",
                    new McpServerToolCreateOptions { Name = "wipe", Destructive = true }),
                McpServerTool.Create(
                    string () => throw new InvalidOperationException("disk full"),
                    new McpServerToolCreateOptions { Name = "fail" }),
            ],
        };

        _server = McpServer.Create(new StreamServerTransport(_toServer.Reader.AsStream(), _toClient.Writer.AsStream()), options);
        _ = _server.RunAsync(_stop.Token);
        var client = await McpClient.CreateAsync(
            new StreamClientTransport(_toServer.Writer.AsStream(), _toClient.Reader.AsStream()),
            cancellationToken: TestContext.Current.CancellationToken);
        _sut = new SdkMcpConnection(client);
    }

    public async ValueTask DisposeAsync()
    {
        await _sut.DisposeAsync();
        await _stop.CancelAsync();
        await _server.DisposeAsync();
        _stop.Dispose();
    }

    [Fact]
    public async Task ListToolsAsync_ReturnsNamesDescriptionsSchemasAndHints()
    {
        // Act
        var tools = await _sut.ListToolsAsync(TestContext.Current.CancellationToken);

        // Assert
        tools.Select(t => t.Name).Should().BeEquivalentTo("add", "wipe", "fail");
        var add = tools.Single(t => t.Name == "add");
        add.Description.Should().Be("Adds two numbers.");
        add.InputSchema.Should().Contain("\"a\"").And.Contain("\"b\"");
        add.ReadOnlyHint.Should().BeTrue();
        tools.Single(t => t.Name == "wipe").DestructiveHint.Should().BeTrue();
    }

    [Fact]
    public async Task CallToolAsync_SendsTheArgumentsAndReturnsTheText()
    {
        // Act
        var result = await _sut.CallToolAsync("add", """{"a": 2, "b": 40}""", TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().Be("42");
    }

    [Fact]
    public async Task CallToolAsync_ToolThrows_ReturnsAnErrorResult()
    {
        // Act
        var result = await _sut.CallToolAsync("fail", "{}", TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task CallToolAsync_UnknownTool_ReturnsAnErrorResult()
    {
        // Act
        var result = await _sut.CallToolAsync("nope", "{}", TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Closed_WhenTheServerStops_Completes()
    {
        // Act
        await _stop.CancelAsync();
        await _server.DisposeAsync();
        await _toClient.Writer.CompleteAsync();

        // Assert
        var closed = _sut.Closed;
#pragma warning disable VSTHRD003 // The connection owns this task; the test only observes it.
        await closed.Bounded();
#pragma warning restore VSTHRD003
        closed.IsCompletedSuccessfully.Should().BeTrue();
    }

    private static int Add(int a, int b) => a + b;
}
