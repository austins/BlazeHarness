using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Sandbox.Ports;
using BlazeHarness.Core.Tests.Integration.Tools;
using BlazeHarness.Core.Tools;
using BlazeHarness.Core.Tools.Shell;
using BlazeHarness.Tests.Shared;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace BlazeHarness.Core.Tests.Integration.Sandbox.Ports;

public sealed class SandboxPortsTests : IDisposable
{
    private const string SkipReason = "Needs Linux with the sandbox, socat, nsenter, python3 and curl.";

    private readonly TempRoot _root = new();
    private readonly TempFolder _scratchFolder = new("bh-scratch-");
    private readonly BackgroundShells _background = new(TimeProvider.System);
    private readonly ShellTool _shell;

    public SandboxPortsTests()
    {
        if (ForwardingTools.Find() is { } tools)
        {
            _background.Ports = new SandboxPorts(tools, TimeProvider.System, new LoggerFake<SandboxPorts>());
        }

        var shell = ShellInfo.DetectCurrent() ?? new ShellInfo("sh", "/bin/sh", IsPowerShell: false);
        _shell = new ShellTool(shell, new MxcShellSandbox(() => SensitivePaths.None), _scratchFolder.Root, _background, TimeProvider.System);
    }

    public static bool CanForward =>
        SandboxSupport.IsAvailableOnPosix &&
        ForwardingTools.Find() is not null &&
        ShellInfo.FindOnPath("python3", Environment.GetEnvironmentVariable("PATH")) is not null &&
        ShellInfo.FindOnPath("curl", Environment.GetEnvironmentVariable("PATH")) is not null;

    public void Dispose()
    {
        _background.Dispose();
        _scratchFolder.Dispose();
        _root.Dispose();
    }

    [Fact(Skip = SkipReason, SkipUnless = nameof(CanForward))]
    public async Task ExecuteAsync_ServerInABackgroundCommand_IsReachedFromThisComputerAndFromLaterCommands()
    {
        // Arrange
        _root.Write("hello.txt", "from the sandbox");
        var port = FreePort();
        var url = string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port}/hello.txt");

        // Act
        var started = await RunAsync(string.Create(CultureInfo.InvariantCulture, $"python3 -m http.server {port} --bind 127.0.0.1"), background: true);
        var fromHost = await GetAsync(url);
        var fromSandbox = await RunAsync($"curl -sS --max-time 5 {url}", background: false);

        // Assert
        started.IsError.Should().BeFalse(started.Output);
        fromHost.Should().Be("from the sandbox");
        fromSandbox.Output.Should().StartWith("from the sandbox");
        _background.Find("bg1")!.Ports.Should().Equal(new ForwardedPort(port, OnHost: true));
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<string> GetAsync(string url)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var giveUp = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        giveUp.CancelAfter(TestTimeouts.Default);
        while (true)
        {
            try
            {
                return await client.GetStringAsync(new Uri(url), giveUp.Token);
            }
            catch (HttpRequestException)
            {
                // Not forwarded yet.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), giveUp.Token);
        }
    }

    private async Task<ToolResult> RunAsync(string command, bool background)
    {
        return await _shell.ExecuteAsync(
            TempRoot.Args(new { command, background, timeout_seconds = 60 }),
            _root.Context.WithShell(ShellMode.Writable),
            TestContext.Current.CancellationToken);
    }
}
