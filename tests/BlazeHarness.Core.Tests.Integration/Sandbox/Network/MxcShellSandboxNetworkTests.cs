using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Sandbox.Network;
using BlazeHarness.Core.Tests.Integration.Tools;
using BlazeHarness.Core.Tools.Shell;
using BlazeHarness.Tests.Shared;
using Microsoft.Extensions.Time.Testing;
using System.Collections.Concurrent;

namespace BlazeHarness.Core.Tests.Integration.Sandbox.Network;

public sealed class MxcShellSandboxNetworkTests : IDisposable
{
    private const string SkipReason = "The sandbox isn't available on this computer, or the test uses POSIX shell syntax.";

    private readonly TempRoot _root = new();
    private readonly FakeTimeProvider _time = new();

    public static bool IsAvailableOnPosix => SandboxSupport.IsAvailableOnPosix;

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
    public async Task Run_WithGate_StillReachesAServerOnItsOwnLoopback()
    {
        // Act
        var result = await RunAsync("mkdir -p w && echo hi > w/index.html && (cd w && python3 -m http.server 18766 --bind 127.0.0.1 >/dev/null 2>&1 &) ; sleep 1.5; echo GOT:$(curl -s -m 5 http://127.0.0.1:18766/index.html); pkill -f 'http.server 18766' ; true", new RefusingGate());

        // Assert
        result.Output.Should().Contain("GOT:hi");
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
