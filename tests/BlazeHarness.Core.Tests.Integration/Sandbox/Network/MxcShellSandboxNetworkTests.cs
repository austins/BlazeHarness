using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Sandbox.Network;
using BlazeHarness.Core.Tests.Integration.Tools;
using BlazeHarness.Core.Tools.Shell;
using BlazeHarness.Tests.Shared;
using Microsoft.Extensions.Time.Testing;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace BlazeHarness.Core.Tests.Integration.Sandbox.Network;

public sealed class MxcShellSandboxNetworkTests : IDisposable
{
    private const string SkipReason = "The sandbox isn't available on this computer, or the test uses POSIX shell syntax.";

    private readonly TempRoot _root = new();
    private readonly FakeTimeProvider _time = new();

    public static bool IsAvailableOnPosix => SandboxSupport.IsAvailableOnPosix;

    public static bool IsAvailableOnLinux => OperatingSystem.IsLinux() && SandboxSupport.IsAvailable;

    public void Dispose() => _root.Dispose();

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_WithGate_AsksTheGateForTheHostAndExplainsTheRefusal()
    {
        // Arrange
        var gate = new RefusingGate();

        // Act
        var result = await RunAsync("curl -s -m 20 http://example.com/ ; echo rc=$?", gate);

        // Assert
        gate.Hosts.Should().Contain("example.com");
        result.Output.Should().Contain("Blocked by the network filter").And.Contain("example.com");
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_WithGate_RefusesAConnectionThatSkipsTheProxy()
    {
        // Arrange
        var gate = new RefusingGate();

        // Act
        var result = await RunAsync("curl -s --noproxy '*' -m 5 http://1.1.1.1/ ; echo rc=$?", gate);

        // Assert
        result.Output.Should().MatchRegex("rc=(28|7)");
        gate.Hosts.Should().BeEmpty();
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(IsAvailableOnPosix))]
    public async Task Run_WithGate_RefusesADirectConnectionToHostLoopback()
    {
        // Arrange
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var gate = new RefusingGate();

        // Act
        var result = await RunAsync(string.Create(CultureInfo.InvariantCulture, $"curl -s --noproxy '*' -m 2 http://127.0.0.1:{port}/ ; echo rc=$?"), gate);

        // Assert
        result.Output.Should().MatchRegex("rc=(28|7)");
        listener.Pending().Should().BeFalse();
        gate.Hosts.Should().BeEmpty();
    }

    [Fact(Skip = "Needs Linux with the sandbox's private network namespace.", SkipUnless = nameof(IsAvailableOnLinux))]
    public async Task Run_WithGate_StillReachesAServerOnItsOwnLoopback()
    {
        // Arrange
        _root.Write("index.html", "hi");
        _root.Write("server.py", """
            from http.server import HTTPServer, SimpleHTTPRequestHandler
            from pathlib import Path

            server = HTTPServer(("127.0.0.1", 0), SimpleHTTPRequestHandler)
            Path("port").write_text(str(server.server_port))
            server.serve_forever()
            """);
        var gate = new RefusingGate();

        const string Command = """
            python3 server.py >server.log 2>&1 &
            server=$!
            trap 'kill "$server" 2>/dev/null; wait "$server" 2>/dev/null' EXIT
            attempts=0
            while [ ! -s port ]; do
                if ! kill -0 "$server" 2>/dev/null || [ "$attempts" -ge 100 ]; then
                    cat server.log >&2
                    exit 1
                fi
                attempts=$((attempts + 1))
                sleep 0.1
            done
            curl -fsS --max-time 5 "http://127.0.0.1:$(cat port)/index.html"
            """;

        // Act
        var result = await RunAsync(Command, gate);

        // Assert
        result.IsError.Should().BeFalse(result.Output);
        result.Output.Should().Be("hi\n[exit code 0]");
        gate.Hosts.Should().BeEmpty();
    }

    private async Task<BlazeHarness.Core.Tools.ToolResult> RunAsync(string command, INetworkGate gate)
    {
        var shell = ShellInfo.DetectCurrent() ?? new ShellInfo("sh", "/bin/sh", IsPowerShell: false);
        using var scratch = new TempFolder("bh-scratch-");
        using var background = new BackgroundShells(_time);
        var tool = new ShellTool(shell, new MxcShellSandbox(() => SensitivePaths.None, time: _time), scratch.Root, background, _time);
        return await tool.ExecuteAsync(
            TempRoot.Args(new { command, timeout_seconds = 60 }),
            _root.Context.WithShell(ShellMode.Writable).WithNetwork(gate),
            TestContext.Current.CancellationToken);
    }

    private sealed class RefusingGate : INetworkGate
    {
        private readonly ConcurrentQueue<string> _hosts = new();

        public IReadOnlyList<string> Hosts => [.. _hosts];

        public IReadOnlyList<string> Refusals => [.. _hosts.Select(host => $"{host} is not allowed.")];

        public Task<NetworkAccess> RequestAsync(string host, CancellationToken cancellationToken)
        {
            _hosts.Enqueue(host);
            return Task.FromResult(NetworkAccess.Refuse($"{host} is not allowed."));
        }
    }
}
