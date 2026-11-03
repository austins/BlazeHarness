using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Sandbox.Ports;
using System.Globalization;

namespace BlazeHarness.Core.Tools.Shell;

/// <summary>
/// The commands of one chat that the model started with <c>background</c>, so they can be listed, read and stopped.
/// </summary>
/// <remarks>
/// The commands live as long as the chat is open: disposing the registry stops all of them. The list is not saved,
/// because a command can't outlive the app.
/// </remarks>
public sealed class BackgroundShells : IDisposable
{
    internal const int MaxRunning = 8;
    internal const int MaxKept = 20;

    private readonly Lock _gate = new();
    private readonly List<BackgroundShell> _jobs = [];
    private readonly TimeProvider _time;
    private int _last;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackgroundShells"/> class.
    /// </summary>
    /// <param name="time">Times each command's time limit and stamps its start.</param>
    public BackgroundShells(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    /// <summary>
    /// Raised when a command starts or ends, on the thread that caused it.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets a snapshot of the commands, oldest first.
    /// </summary>
    public IReadOnlyList<BackgroundShellInfo> Jobs
    {
        get
        {
            lock (_gate)
            {
                return [.. _jobs.Select(job => job.Info)];
            }
        }
    }

    /// <summary>
    /// Gets or sets the forwarding of the sandboxed commands' ports, or null when this computer can't forward them.
    /// The registry disposes it.
    /// </summary>
    internal SandboxPorts? Ports { get; set; }

    internal int RunningCount
    {
        get
        {
            lock (_gate)
            {
                return _jobs.Count(job => job.IsRunning);
            }
        }
    }

    /// <summary>
    /// Stop a command and everything it started.
    /// </summary>
    /// <param name="id">The id from <see cref="Jobs"/>.</param>
    /// <returns>False when there is no running command with that id.</returns>
    public bool Stop(string id) => Find(id)?.Stop() ?? false;

    /// <summary>
    /// Stop every command.
    /// </summary>
    public void Dispose()
    {
        List<BackgroundShell> jobs;
        lock (_gate)
        {
            _disposed = true;
            jobs = [.. _jobs];
        }

        foreach (var job in jobs)
        {
            job.Stop();
        }

#pragma warning disable IDISP007 // The registry owns the forwarding it is given.
        Ports?.Dispose();
#pragma warning restore IDISP007
    }

    internal BackgroundShell? Find(string id)
    {
        lock (_gate)
        {
            return _jobs.Find(job => string.Equals(job.Id, id, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal string Ids()
    {
        lock (_gate)
        {
            return _jobs.Count == 0 ? "none" : string.Join(", ", _jobs.Select(job => job.Id));
        }
    }

    /// <summary>
    /// Take over a started command.
    /// </summary>
    /// <param name="process">The started command.</param>
    /// <param name="command">The command text.</param>
    /// <param name="timeLimit">How long it may run.</param>
    /// <param name="output">Collects what it prints.</param>
    /// <param name="readers">The task that fills <paramref name="output"/>.</param>
    /// <param name="ports">The command's part in port forwarding, which the command disposes when it ends, or null.</param>
    /// <returns>The tracked command, or null when the registry was disposed, in which case the process and <paramref name="ports"/> are left to the caller.</returns>
    internal BackgroundShell? Start(IShellProcess process, string command, TimeSpan timeLimit, ShellOutput output, Task readers, SandboxMember? ports = null)
    {
        BackgroundShell job;
        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            _last++;
            job = new BackgroundShell(
                string.Create(CultureInfo.InvariantCulture, $"bg{_last}"),
                command,
                timeLimit,
                _time.GetUtcNow(),
                process,
                ports,
                output,
                readers,
                _time,
                () => Changed?.Invoke(this, EventArgs.Empty));
            _jobs.Add(job);
            var surplus = _jobs.Count - MaxKept;
            for (var i = 0; i < _jobs.Count && surplus > 0;)
            {
                if (_jobs[i].IsRunning || ReferenceEquals(_jobs[i], job))
                {
                    i++;
                }
                else
                {
                    _jobs.RemoveAt(i);
                    surplus--;
                }
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return job;
    }
}
