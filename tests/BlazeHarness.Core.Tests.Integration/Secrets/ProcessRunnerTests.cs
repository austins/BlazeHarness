using BlazeHarness.Core.Secrets;
using System.Diagnostics;

namespace BlazeHarness.Core.Tests.Integration.Secrets;

public sealed class ProcessRunnerTests : IDisposable
{
    private readonly CancellationTokenSource _cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    private readonly TempFolder _temp = new("bh-proc-");

    public ProcessRunnerTests()
    {
        Directory.CreateDirectory(_temp.Root);
    }

    public static bool IsWindows => OperatingSystem.IsWindows();

    public void Dispose()
    {
        _cts.Dispose();
        _temp.Dispose();
    }

    [Fact(Skip = "Needs a POSIX shell.", SkipWhen = nameof(IsWindows))]
    public async Task RunAsync_Cancelled_KillsTheProcess()
    {
        // Arrange
        var pidFile = Path.Combine(_temp.Root, "pid");
        var run = Task.Run(async () => await ProcessRunner.RunAsync("sh", ["-c", "echo $$ > \"$1.tmp\" && mv \"$1.tmp\" \"$1\" && exec sleep 300", "sh", pidFile], null, _cts.Token), TestContext.Current.CancellationToken);
        while (!File.Exists(pidFile))
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        var pid = int.Parse((await File.ReadAllTextAsync(pidFile, TestContext.Current.CancellationToken)).Trim(), System.Globalization.CultureInfo.InvariantCulture);
        using var victim = Process.GetProcessById(pid);

        // Act
        await _cts.CancelAsync();
        #pragma warning disable VSTHRD003 // The task was started by this test.
        var act = async () => await run.Bounded();
#pragma warning restore VSTHRD003

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>().WaitAsync(TestTimeouts.Default, TestContext.Current.CancellationToken);
        await victim.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TestTimeouts.Default, TestContext.Current.CancellationToken);
        victim.HasExited.Should().BeTrue();
    }
}
