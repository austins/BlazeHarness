using BlazeHarness.Core.Tools;
using System.Text.Json;

namespace BlazeHarness.Core.Tests.Integration.Tools;

public sealed class ShellToolTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly ShellTool _sut;

    public ShellToolTests()
    {
        _sut = new ShellTool(ShellInfo.DetectCurrent() ?? new ShellInfo("sh", "/bin/sh", IsPowerShell: false));
    }

    public static bool IsWindows => OperatingSystem.IsWindows();

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
        // Act
        var result = await RunAsync(new { command = "echo started; sleep 30", timeout_seconds = 1 });

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().StartWith("started").And.Contain("ran longer than 1 seconds");
    }

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public async Task ExecuteAsync_Cancelled_KillsTheWholeProcessTree()
    {
        // Arrange
        var marker = Path.Combine(_workspace.Root, "survived.txt");
        var command = $"(sleep 2; echo alive > '{marker}') & echo ready; wait";

        // Act
        var act = () => RunUntilCancelledAsync(command);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        await Task.Delay(2500, TestContext.Current.CancellationToken);
        File.Exists(marker).Should().BeFalse();
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
        var sut = new ShellTool(new ShellInfo("bash", Path.Combine(_workspace.Root, "missing-shell"), IsPowerShell: false));

        // Act
        var result = await sut.ExecuteAsync(TempWorkspace.Args(new { command = "true" }), _workspace.Context, TestContext.Current.CancellationToken);

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().StartWith("Could not start bash");
    }

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public void Summarize_MultiLineCommand_ShowsTheFirstLine()
    {
        // Arrange
        using var document = JsonDocument.Parse("""{"command":"cd src\nmake"}""");

        // Act
        var summary = _sut.Summarize(document.RootElement);

        // Assert
        summary.Should().Be("cd src …");
    }

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public void Properties_Always_RequireApprovalAndNameTheShell()
    {
        // Assert
        _sut.IsReadOnly.Should().BeFalse();
        _sut.Name.Should().Be("shell");
        _sut.Description.Should().Contain(ShellInfo.DetectCurrent()!.Name);
        using var schema = JsonDocument.Parse(_sut.InputSchema);
        schema.RootElement.GetProperty("required")[0].GetString().Should().Be("command");
    }

    private async Task<ToolResult> RunUntilCancelledAsync(string command)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = _sut.ExecuteAsync(TempWorkspace.Args(new { command }), _workspace.Context, cts.Token);
        await Task.Delay(500, TestContext.Current.CancellationToken);
        await cts.CancelAsync();
        return await run;
    }

    private Task<ToolResult> RunAsync(object args) => _sut.ExecuteAsync(TempWorkspace.Args(args), _workspace.Context, TestContext.Current.CancellationToken);
}
