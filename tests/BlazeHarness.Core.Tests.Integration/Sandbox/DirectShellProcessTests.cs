using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Tools;

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
        Path.GetFullPath(stdout.Trim()).Should().Be(Path.GetFullPath(_temp.Root));
        stderr.Trim().Should().Be("oops");
        exit.Should().Be(3);
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

    [Fact]
    public async Task StartAsync_MissingExecutable_ThrowsIOExceptionNamingTheShell()
    {
        // Arrange
        var launch = Launch("true") with { Shell = new ShellInfo("ghost", Path.Combine(_temp.Root, "no-such-shell"), IsPowerShell: false) };

        // Act
        var act = () => _sandbox.StartAsync(launch, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<IOException>().WithMessage("Could not start ghost*");
    }

    private ShellLaunch Launch(string command)
    {
        return new ShellLaunch(Sh, command, _temp.Root, ShellMode.Unconfined, _temp.Root, TestTimeouts.Default);
    }
}
