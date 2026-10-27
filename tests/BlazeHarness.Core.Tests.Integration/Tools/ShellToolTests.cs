using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Tools;
using Microsoft.Extensions.Time.Testing;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Integration.Tools;

public sealed class ShellToolTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly FakeTimeProvider _time = new();
    private readonly ShellTool _sut;

    public ShellToolTests()
    {
        _sut = new ShellTool(Sh, new NoShellSandbox("none"), _workspace.Root, _time);
    }

    public static bool IsWindows => OperatingSystem.IsWindows();

    private static ShellInfo Sh { get; } = new("sh", "/bin/sh", IsPowerShell: false);

    private ToolContext Context => _workspace.Context.WithShell(ShellMode.Unconfined);

    public void Dispose() => _workspace.Dispose();

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public async Task ExecuteAsync_SuccessfulCommand_ReturnsOutputAndExitCode()
    {
        // Act
        var result = await RunAsync(new { command = "echo hello" });

        // Assert
        result.Should().Be(ToolResult.Ok("hello\n[exit code 0]"));
    }

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public async Task ExecuteAsync_FailingCommand_ReturnsAnErrorWithTheExitCode()
    {
        // Act
        var result = await RunAsync(new { command = "echo oops >&2; exit 3" });

        // Assert
        result.Should().Be(ToolResult.Error("oops\n[exit code 3]"));
    }

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public async Task ExecuteAsync_Command_RunsInTheWorkingFolder()
    {
        // Arrange
        _workspace.Write("marker.txt", "x");

        // Act
        var result = await RunAsync(new { command = "pwd; ls" });

        // Assert
        result.Output.Should().Contain(_workspace.Root).And.Contain("marker.txt");
    }

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public async Task ExecuteAsync_CommandThatReadsInput_SeesEndOfInputInsteadOfWaiting()
    {
        // Act
        var result = await RunAsync(new { command = "cat; echo done" });

        // Assert
        result.Should().Be(ToolResult.Ok("done\n[exit code 0]"));
    }

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public async Task ExecuteAsync_LongRunningCommand_StopsAtTheTimeoutAndKeepsOutputSoFar()
    {
        // Arrange
        var ready = Path.Combine(_workspace.Root, "ready.txt");

        // Act
        var run = RunAsync(new { command = $"echo started; echo ready > '{ready}'; sleep 30", timeout_seconds = 1 });
        await WaitForFileAsync(ready);
        var result = await _time.AdvanceUntilAsync(run, TimeSpan.FromSeconds(1));

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().StartWith("started").And.Contain("ran longer than 1 seconds");
    }

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public async Task ExecuteAsync_Cancelled_KillsTheWholeProcessTree()
    {
        // Arrange
        var ready = Path.Combine(_workspace.Root, "ready.txt");
        var command = $"sleep 300 & echo ready > '{ready}'; wait";

        // Act
        // The clock never advances, so the grace period for output can't end: the call only returns when the child that holds the output pipe is gone.
        var act = () => RunUntilCancelledAsync(command, ready);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public async Task ExecuteAsync_HugeOutput_KeepsTheStartAndTheEnd()
    {
        // Act
        var result = await RunAsync(new { command = "seq 1 100000" });

        // Assert
        result.Output.Length.Should().BeLessThan(ToolSet.MaxOutputCharacters);
        result.Output.Should().StartWith("1\n2\n").And.Contain("characters left out").And.EndWith("100000\n[exit code 0]");
    }

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public async Task ExecuteAsync_TimeoutOutOfRange_Throws()
    {
        // Act
        var act = () => RunAsync(new { command = "true", timeout_seconds = 100000 });

        // Assert
        await act.Should().ThrowAsync<ToolArgumentException>();
    }

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public async Task ExecuteAsync_ShellThatCannotStart_ReturnsAnError()
    {
        // Arrange
        var sut = new ShellTool(new ShellInfo("bash", Path.Combine(_workspace.Root, "missing-shell"), IsPowerShell: false), new NoShellSandbox("none"), _workspace.Root, _time);

        // Act
        var result = await sut.ExecuteAsync(TempWorkspace.Args(new { command = "true" }), Context, TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().StartWith("Could not start bash");
    }

    [Fact]
    public void Summarize_MultiLineCommand_ShowsTheFirstLine()
    {
        // Arrange
        using var document = JsonDocument.Parse("""{"command":"cd src\nmake"}""");

        // Act
        var summary = _sut.Summarize(document.RootElement);

        // Assert
        summary.Should().Be("cd src …");
    }

    [Fact]
    public void Properties_Always_RequireApprovalAndNameTheShell()
    {
        // Assert
        _sut.IsReadOnly.Should().BeFalse();
        _sut.Name.Should().Be("shell");
        _sut.Description.Should().Contain(Sh.Name);
        using var schema = JsonDocument.Parse(_sut.InputSchema);
        schema.RootElement.GetProperty("required")[0].GetString().Should().Be("command");
    }

    private static async Task WaitForFileAsync(string path)
    {
        using var giveUp = new CancellationTokenSource(TestTimeouts.Default);
        while (!File.Exists(path))
        {
            await Task.Delay(10, giveUp.Token);
        }
    }

    private async Task<ToolResult> RunUntilCancelledAsync(string command, string readyFile)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = _sut.ExecuteAsync(TempWorkspace.Args(new { command }), Context, cts.Token);
        await WaitForFileAsync(readyFile);
        await cts.CancelAsync();
        return await run.Bounded();
    }

    private Task<ToolResult> RunAsync(object args) => _sut.ExecuteAsync(TempWorkspace.Args(args), Context, TestContext.Current.CancellationToken);
}
