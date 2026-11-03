using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Tools;
using BlazeHarness.Core.Tools.Shell;
using BlazeHarness.Tests.Shared;
using Microsoft.Extensions.Time.Testing;
using System.Globalization;

namespace BlazeHarness.Core.Tests.Integration.Tools.Shell;

public sealed class BackgroundShellTests : IDisposable
{
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(1);

    private readonly TempRoot _root = new();
    private readonly FakeTimeProvider _time = new();
    private readonly BackgroundShells _background;
    private readonly ShellTool _shell;
    private readonly ShellJobsTool _jobs;

    public BackgroundShellTests()
    {
        _background = new BackgroundShells(_time);
        _shell = new ShellTool(Sh, new NoShellSandbox("none"), _root.Root, _background, _time);
        _jobs = new ShellJobsTool(_background, _time);
    }

    public static bool IsWindows => OperatingSystem.IsWindows();

    private static ShellInfo Sh { get; } = new("sh", "/bin/sh", IsPowerShell: false);

    private ToolContext Context => _root.Context.WithShell(ShellMode.Unconfined);

    public void Dispose()
    {
        _background.Dispose();
        _root.Dispose();
    }

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public async Task ExecuteAsync_BackgroundCommandThatEndsRightAway_ReturnsItsOutputAndExitCode()
    {
        // Act
        var result = await RunShellAsync(new { command = "echo hello; exit 3", background = true });

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().StartWith("hello").And.Contain("bg1 ended right after it started: exited with code 3");
        _background.Jobs.Should().ContainSingle().Which.Status.Should().Be(BackgroundShellStatus.Exited);
    }

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public async Task ExecuteAsync_BackgroundCommandThatKeepsRunning_ReturnsTheIdAndTheTimeLimit()
    {
        // Act
        var result = await StartSleepAsync(timeoutSeconds: 120);

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().StartWith("up").And.Contain("Started in the background as bg1").And.Contain("120 seconds");
        _background.Jobs.Should().ContainSingle().Which.Status.Should().Be(BackgroundShellStatus.Running);
    }

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public async Task ExecuteAsync_BackgroundCommandPastItsTimeLimit_IsStoppedAndReportedAsTimedOut()
    {
        // Arrange
        await StartSleepAsync(timeoutSeconds: 10);
        var job = _background.Find("bg1")!;

        // Act
        await _time.AdvanceUntilAsync(EndAsync(job), Step);

        // Assert
        _background.Jobs.Should().ContainSingle().Which.Status.Should().Be(BackgroundShellStatus.TimedOut);
        job.Describe().Should().Contain("10-second time limit");
    }

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public async Task ExecuteAsync_BackgroundWithTimeoutOutOfRange_Throws()
    {
        // Act
        var act = () => RunShellAsync(new { command = "true", background = true, timeout_seconds = 100000 });

        // Assert
        await act.Should().ThrowAsync<ToolArgumentException>();
    }

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public async Task ExecuteAsync_BackgroundWhenTheLimitIsReached_RefusesToStartAnother()
    {
        // Arrange
        for (var i = 0; i < BackgroundShells.MaxRunning; i++)
        {
            await StartSleepAsync();
        }

        // Act
        var result = await RunShellAsync(new { command = "true", background = true });

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain("This command was not run").And.Contain("shell_jobs");
        _background.Jobs.Should().HaveCount(BackgroundShells.MaxRunning);
    }

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public async Task Dispose_WithRunningCommand_StopsIt()
    {
        // Arrange
        await StartSleepAsync();
        var job = _background.Find("bg1")!;

        // Act
        _background.Dispose();
        await _time.AdvanceUntilAsync(EndAsync(job), Step);

        // Assert
        _background.Jobs.Should().ContainSingle().Which.Status.Should().Be(BackgroundShellStatus.Stopped);
    }

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public async Task Start_AfterDispose_RunsNothing()
    {
        // Arrange
        _background.Dispose();

        // Act
        var result = await RunShellAsync(new { command = "sleep 300", background = true });

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain("closing");
        _background.Jobs.Should().BeEmpty();
    }

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public async Task Stop_RunningCommand_RaisesChangedWhenItEnds()
    {
        // Arrange
        await StartSleepAsync();
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _background.Changed += (_, _) =>
        {
            if (_background.Jobs.Count == 1 && _background.Jobs[0].Status == BackgroundShellStatus.Stopped)
            {
                ended.TrySetResult();
            }
        };

        // Act
        var stopped = _background.Stop("bg1");
        await _time.AdvanceUntilAsync(ended.Task, Step);

        // Assert
        stopped.Should().BeTrue();
        _background.Stop("bg1").Should().BeFalse();
        _background.Stop("nope").Should().BeFalse();
    }

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public async Task Start_MoreThanTheKeptLimit_DropsTheOldestFinishedCommands()
    {
        // Arrange
        for (var i = 0; i < BackgroundShells.MaxKept + 3; i++)
        {
            await RunShellAsync(new { command = "true", background = true });
        }

        // Act
        var jobs = _background.Jobs;

        // Assert
        jobs.Should().HaveCount(BackgroundShells.MaxKept);
        jobs[0].Id.Should().Be("bg4");
        jobs[^1].Id.Should().Be("bg23");
    }

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public async Task ExecuteAsync_JobsList_ShowsStatusAndCommand()
    {
        // Arrange
        await StartSleepAsync();

        // Act
        var result = await RunJobsAsync(new { action = "list" });

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().StartWith("bg1  running").And.Contain("sleep 300");
    }

    [Fact]
    public async Task ExecuteAsync_JobsListWithNothingStarted_SaysSo()
    {
        // Act
        var result = await RunJobsAsync(new { action = "list" });

        // Assert
        result.Should().Be(ToolResult.Ok("No background commands."));
    }

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public async Task ExecuteAsync_JobsOutputOfRunningCommand_ReturnsWhatItPrintedSoFar()
    {
        // Arrange
        await StartSleepAsync();

        // Act
        var result = await RunJobsAsync(new { action = "output", id = "bg1" });

        // Assert
        result.Output.Should().StartWith("up").And.EndWith("[bg1: running]");
    }

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public async Task ExecuteAsync_JobsOutputWithWait_ReturnsWhenTheCommandEnds()
    {
        // Arrange
        await StartSleepAsync(timeoutSeconds: 20);

        // Act
        var run = RunJobsAsync(new { action = "output", id = "bg1", wait_seconds = 60 });
        var result = await _time.AdvanceUntilAsync(run, Step);

        // Assert
        result.Output.Should().Contain("20-second time limit");
    }

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public async Task ExecuteAsync_JobsStop_StopsTheCommandAndReportsIt()
    {
        // Arrange
        await StartSleepAsync();

        // Act
        var run = RunJobsAsync(new { action = "stop", id = "bg1" });
        var result = await _time.AdvanceUntilAsync(run, Step);

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().StartWith("Stopped bg1.").And.EndWith("[bg1: stopped]");
        _background.Jobs.Should().ContainSingle().Which.Status.Should().Be(BackgroundShellStatus.Stopped);
    }

    [Fact(Skip = "These tests use POSIX shell syntax.", SkipWhen = nameof(IsWindows))]
    public async Task ExecuteAsync_JobsStopOfEndedCommand_SaysItHadEnded()
    {
        // Arrange
        await RunShellAsync(new { command = "true", background = true });

        // Act
        var result = await RunJobsAsync(new { action = "stop", id = "bg1" });

        // Assert
        result.IsError.Should().BeFalse();
        result.Output.Should().Contain("had already ended");
    }

    [Fact]
    public async Task ExecuteAsync_JobsUnknownId_ReturnsAnErrorNamingTheKnownIds()
    {
        // Act
        var result = await RunJobsAsync(new { action = "stop", id = "bg9" });

        // Assert
        result.IsError.Should().BeTrue();
        result.Output.Should().Contain("no background command 'bg9'").And.Contain("none");
    }

    [Fact]
    public async Task ExecuteAsync_JobsUnknownAction_ReturnsAnError()
    {
        // Act
        var result = await RunJobsAsync(new { action = "restart" });

        // Assert
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_JobsWaitOutOfRange_Throws()
    {
        // Arrange
        await RunShellAsync(new { command = "true", background = true });

        // Act
        var act = () => RunJobsAsync(new { action = "output", id = "bg1", wait_seconds = 1000 });

        // Assert
        await act.Should().ThrowAsync<ToolArgumentException>();
    }

    [Fact]
    public void Properties_Always_AllowStoppingWithoutApproval()
    {
        // Assert
        _jobs.Name.Should().Be("shell_jobs");
        _jobs.IsReadOnly.Should().BeTrue();
    }

    private async Task<ToolResult> StartSleepAsync(int timeoutSeconds = 3600)
    {
        var id = string.Create(CultureInfo.InvariantCulture, $"bg{_background.Jobs.Count + 1}");
        var run = RunShellAsync(new { command = "echo up; sleep 300", background = true, timeout_seconds = timeoutSeconds });
        await WaitForOutputAsync(id);
        return await _time.AdvanceUntilAsync(run, Step);
    }

    private static async Task EndAsync(BackgroundShell job)
    {
#pragma warning disable VSTHRD003 // The job's own task is the thing the test waits for.
        await job.Exit;
#pragma warning restore VSTHRD003
    }

    private async Task WaitForOutputAsync(string id)
    {
        using var giveUp = new CancellationTokenSource(TestTimeouts.Default);
        while (_background.Find(id)?.Output.ToString().Contains("up", StringComparison.Ordinal) != true)
        {
            await Task.Delay(10, giveUp.Token);
        }
    }

    private Task<ToolResult> RunShellAsync(object args) => _shell.ExecuteAsync(TempRoot.Args(args), Context, TestContext.Current.CancellationToken);

    private Task<ToolResult> RunJobsAsync(object args) => _jobs.ExecuteAsync(TempRoot.Args(args), Context, TestContext.Current.CancellationToken);
}
