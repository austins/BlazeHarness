using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Sandbox.Ports;
using System.Globalization;

namespace BlazeHarness.Core.Tools.Shell;

/// <summary>
/// One command the model started with <c>background</c>, which keeps running after the call that started it returned.
/// </summary>
internal sealed class BackgroundShell
{
    private readonly Lock _gate = new();
    private readonly IShellProcess _process;
    private readonly SandboxMember? _ports;
    private BackgroundShellStatus _reason = BackgroundShellStatus.Exited;
    private bool _finished;
    private int _exitCode;

    public BackgroundShell(string id, string command, TimeSpan timeLimit, DateTimeOffset startedAt, IShellProcess process, SandboxMember? ports, ShellOutput output, Task readers, TimeProvider time, Action ended)
    {
        Id = id;
        Command = command;
        TimeLimit = timeLimit;
        StartedAt = startedAt;
        Output = output;
        _process = process;
        _ports = ports;
        Exit = RunAsync(readers, timeLimit, time);

        // After the task is complete, so a listener that reads the status sees the end.
        _ = Exit.ContinueWith(_ => ended(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public string Id { get; }

    public string Command { get; }

    public TimeSpan TimeLimit { get; }

    public DateTimeOffset StartedAt { get; }

    public ShellOutput Output { get; }

    /// <summary>
    /// Gets a task that completes once the command has ended and its output was read.
    /// </summary>
    public Task Exit { get; }

    /// <summary>
    /// Gets the command's ports that are forwarded out of its sandbox.
    /// </summary>
    public IReadOnlyList<ForwardedPort> Ports => _ports?.Ports ?? [];

    public int ExitCode => Volatile.Read(ref _exitCode);

    public bool IsRunning => !Exit.IsCompleted;

    public BackgroundShellStatus Status => IsRunning ? BackgroundShellStatus.Running : _reason;

    public BackgroundShellInfo Info => new(Id, Command, Status, IsRunning ? null : ExitCode, StartedAt, TimeLimit);

    public string Describe()
    {
        return Status switch
        {
            BackgroundShellStatus.Running => "running",
            BackgroundShellStatus.Stopped => "stopped",
            BackgroundShellStatus.TimedOut => string.Create(CultureInfo.InvariantCulture, $"stopped: it reached its {TimeLimit.TotalSeconds:0}-second time limit"),
            _ => string.Create(CultureInfo.InvariantCulture, $"exited with code {ExitCode}"),
        };
    }

    /// <summary>
    /// Stop the command and everything it started.
    /// </summary>
    /// <returns>False when it had already ended.</returns>
    public bool Stop() => Kill(BackgroundShellStatus.Stopped);

    private bool Kill(BackgroundShellStatus reason)
    {
        lock (_gate)
        {
            if (_finished)
            {
                return false;
            }

            _reason = reason;
            _process.Kill();
            return true;
        }
    }

    private async Task RunAsync(Task readers, TimeSpan timeLimit, TimeProvider time)
    {
        using var limit = new CancellationTokenSource(timeLimit, time);
        try
        {
            using var registration = limit.Token.Register(() => Kill(BackgroundShellStatus.TimedOut));
            Volatile.Write(ref _exitCode, await _process.WaitAsync(CancellationToken.None));
#pragma warning disable VSTHRD003 // The caller started the readers for this command and hands them over on purpose.
            await ShellTool.FinishReadingAsync(readers, time);
#pragma warning restore VSTHRD003
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A process that can't be waited for any more has ended as far as the chat is concerned.
            Volatile.Write(ref _exitCode, -1);
        }
        finally
        {
            lock (_gate)
            {
                _finished = true;
#pragma warning disable IDISP007 // This object owns the process it is given.
                _process.Dispose();
                _ports?.Dispose();
#pragma warning restore IDISP007
            }
        }
    }
}
