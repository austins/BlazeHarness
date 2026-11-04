using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Tools.Files;
using BlazeHarness.Core.Tools.Shell;
using BlazeHarness.Tests.Shared;
using System.Diagnostics;

namespace BlazeHarness.Core.Tests.Integration.Sandbox;

public sealed class DirectShellProcessTests : IDisposable
{
    private static readonly ShellInfo Sh = new("sh", "/bin/sh", IsPowerShell: false);

    private readonly TempFolder _temp = new("bh-direct-");
    private readonly NoShellSandbox _sandbox = new("none");

    public DirectShellProcessTests()
    {
        Directory.CreateDirectory(_temp.Root);
    }

    public static bool IsWindows => OperatingSystem.IsWindows();

    public static bool CannotRunWindowsShell => !IsWindows || ShellInfo.DetectCurrent() is null;

    public static bool CannotRunTimeout => IsWindows || ShellInfo.FindOnPath("timeout", Environment.GetEnvironmentVariable("PATH")) is null;

    public void Dispose() => _temp.Dispose();

    [Fact(Skip = "Needs /bin/sh.", SkipWhen = nameof(IsWindows))]
    public async Task StartAsync_Unconfined_RunsInTheWorkingFolderAndCapturesBothStreams()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var process = await _sandbox.StartAsync(Launch("pwd; echo oops >&2; exit 3"), ct);

        // Act
        using var stdoutReader = new StreamReader(process.Output);
        using var stderrReader = new StreamReader(process.Error);
        var stdout = await stdoutReader.ReadToEndAsync(ct).Bounded();
        var stderr = await stderrReader.ReadToEndAsync(ct).Bounded();
        var exit = await process.WaitAsync(ct).Bounded();

        // Assert
        Path.GetFullPath(stdout.Trim()).Should().Be(RootedPath.CanonicalRoot(_temp.Root));
        stderr.Trim().Should().Be("oops");
        exit.Should().Be(3);
    }

    [Fact(Skip = "Needs /bin/sh.", SkipWhen = nameof(IsWindows))]
    public async Task WaitAsync_ExitedParent_StopsItsBackgroundChild()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var process = await _sandbox.StartAsync(Launch("nohup sleep 300 & echo $!; echo done"), ct);
        using var stdoutReader = new StreamReader(process.Output);
        var pid = int.Parse((await stdoutReader.ReadLineAsync(ct).AsTask().Bounded())!, System.Globalization.CultureInfo.InvariantCulture);
        // Act
        var exit = await process.WaitAsync(ct).Bounded();
        var remaining = await stdoutReader.ReadToEndAsync(ct).Bounded();

        // Assert
        exit.Should().Be(0);
        remaining.Should().Be("done\n");
        await AssertChildExitedAsync(pid, ct);
    }

    [Fact(Skip = "Needs Windows with PowerShell.", SkipWhen = nameof(CannotRunWindowsShell))]
    public async Task WaitAsync_ExitedWindowsParent_StopsItsBackgroundChild()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        const string Command = "$child = Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList '-NoLogo', '-NoProfile', '-NonInteractive', '-Command', '[Threading.ManualResetEvent]::new($false).WaitOne()' -NoNewWindow -PassThru; $child.Id; exit 0";
        using var process = await _sandbox.StartAsync(Launch(Command) with { Shell = ShellInfo.DetectCurrent()! }, ct);
        using var reader = new StreamReader(process.Output);
        var pid = int.Parse((await reader.ReadLineAsync(ct).AsTask().Bounded())!, System.Globalization.CultureInfo.InvariantCulture);
        // Act
        var exit = await process.WaitAsync(ct).Bounded();
        var remaining = await reader.ReadToEndAsync(ct).Bounded();

        // Assert
        exit.Should().Be(0);
        remaining.Should().BeEmpty();
        await AssertChildExitedAsync(pid, ct);
    }

    [Theory(Skip = "Needs Windows with PowerShell.", SkipWhen = nameof(CannotRunWindowsShell))]
    [InlineData("Get-Item nope", 1)]
    [InlineData("cmd.exe /c exit 5", 1)]
    [InlineData("exit 5", 5)]
    public async Task WaitAsync_FailingWindowsCommand_PreservesExitCode(string command, int expected)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var process = await _sandbox.StartAsync(Launch(command) with { Shell = ShellInfo.DetectCurrent()! }, ct);
        using var stdoutReader = new StreamReader(process.Output);
        using var stderrReader = new StreamReader(process.Error);
        var stdout = stdoutReader.ReadToEndAsync(ct);
        var stderr = stderrReader.ReadToEndAsync(ct);

        // Act
        var exit = await process.WaitAsync(ct).Bounded();
        await Task.WhenAll(stdout, stderr).Bounded();

        // Assert
        exit.Should().Be(expected);
    }

    [Fact(Skip = "Needs /bin/sh.", SkipWhen = nameof(IsWindows))]
    public async Task Dispose_RunningParent_StopsTheWholeGroup()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var process = await _sandbox.StartAsync(Launch("sleep 300 & echo $!; wait"), ct);
        using var stdoutReader = new StreamReader(process.Output);
        var pid = int.Parse((await stdoutReader.ReadLineAsync(ct).AsTask().Bounded())!, System.Globalization.CultureInfo.InvariantCulture);

        // Act
        var waiting = process.WaitAsync(ct);
#pragma warning disable IDISP017 // Explicit disposal while the exit wait is pending is the behavior under test.
        process.Dispose();
#pragma warning restore IDISP017
        var exit = await waiting.Bounded();

        // Assert
        exit.Should().NotBe(0);
        await AssertChildExitedAsync(pid, ct);
    }

    [Fact(Skip = "Needs /bin/sh.", SkipWhen = nameof(IsWindows))]
    public async Task StartAsync_StdinIsClosed_SoACommandThatReadsInputEnds()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var process = await _sandbox.StartAsync(Launch("cat; echo done"), ct);

        // Act
        using var stdoutReader = new StreamReader(process.Output);
        var stdout = await stdoutReader.ReadToEndAsync(ct).Bounded();
        var exit = await process.WaitAsync(ct).Bounded();

        // Assert
        stdout.Trim().Should().Be("done");
        exit.Should().Be(0);
    }

    [Fact(Skip = "Needs /bin/sh.", SkipWhen = nameof(IsWindows))]
    public async Task Kill_RunningProcess_EndsItAndKillingAgainIsHarmless()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var process = await _sandbox.StartAsync(Launch("exec sleep 300"), ct);

        // Act
        process.Kill();
        var exit = await process.WaitAsync(ct).Bounded();
        var again = process.Kill;

        // Assert
        exit.Should().NotBe(0);
        again.Should().NotThrow();
    }

    [Theory(Skip = "Needs a POSIX shell and timeout on PATH.", SkipWhen = nameof(CannotRunTimeout))]
    [InlineData("echo start; timeout 300 sleep 300")]
    [InlineData("timeout 300 sh -c 'echo start; exec sleep 300'; :")]
    public async Task Kill_ChildInAnotherProcessGroup_ClosesItsOutput(string command)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        using var process = await _sandbox.StartAsync(Launch(command), ct);
        using var reader = new StreamReader(process.Output);
        var started = await reader.ReadLineAsync(ct).AsTask().Bounded();

        // Act
        process.Kill();
        var exit = await process.WaitAsync(ct).Bounded();
        var remaining = await reader.ReadToEndAsync(ct).Bounded();

        // Assert
        started.Should().Be("start");
        exit.Should().NotBe(0);
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_MissingExecutable_ThrowsIOExceptionNamingTheShell()
    {
        // Arrange
        var launch = Launch("true") with { Shell = new ShellInfo("ghost", Path.Combine(_temp.Root, "no-such-shell"), IsPowerShell: false) };

        // Act
        var act = () => _sandbox.StartAsync(launch, TestContext.Current.CancellationToken);

        // Assert
        var exception = await act.Should().ThrowAsync<IOException>().WithMessage("Could not start ghost: *");
        exception.Which.Message.Should().Contain(exception.Which.InnerException!.Message);
    }

    private ShellLaunch Launch(string command)
    {
        return new ShellLaunch(Sh, command, _temp.Root, ShellMode.Unconfined, _temp.Root, TestTimeouts.Default);
    }

    private static async Task AssertChildExitedAsync(int pid, CancellationToken cancellationToken)
    {
        Process child;
        try
        {
            child = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            // The child has already been reaped.
            return;
        }

        using (child)
        {
            await child.WaitForExitAsync(cancellationToken).Bounded();
            child.HasExited.Should().BeTrue();
        }
    }
}
