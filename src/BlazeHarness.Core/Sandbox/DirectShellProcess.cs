using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace BlazeHarness.Core.Sandbox;

/// <summary>
/// A command running as an ordinary child process of the app.
/// </summary>
internal sealed class DirectShellProcess : IShellProcess
{
    private readonly Process _process;
    private readonly WindowsProcessJob _job;

    private DirectShellProcess(Process process, WindowsProcessJob job)
    {
        _process = process;
        _job = job;
    }

    /// <inheritdoc />
    public Stream Output => _process.StandardOutput.BaseStream;

    /// <inheritdoc />
    public Stream Error => _process.StandardError.BaseStream;

    /// <summary>
    /// Start a command whose descendants are owned until its shell exits or the process is disposed.
    /// </summary>
    /// <param name="launch">The shell command and its working folder.</param>
    /// <returns>The running command.</returns>
    public static IShellProcess Start(ShellLaunch launch)
    {
        if (!OperatingSystem.IsWindows())
        {
            return PosixShellProcess.Start(launch);
        }

        var info = new ProcessStartInfo(launch.Shell.Executable)
        {
            WorkingDirectory = launch.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // The shell cannot start user code until it belongs to the job.
        var bootstrap = (launch.Shell.IsPowerShell ? "$null = [Console]::ReadLine()\n" : "IFS= read -r _\n") + launch.Command;
        foreach (var argument in launch.Shell.ArgumentsFor(bootstrap))
        {
            info.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = info };
        WindowsProcessJob? job = null;
        try
        {
            job = new WindowsProcessJob();
            process.Start();
            job.Assign(process);
            process.StandardInput.WriteLine("run");
            process.StandardInput.Close();
            return new DirectShellProcess(process, job);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            // Kill an unassigned shell before closing stdin, which would otherwise release its gate.
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception stopError) when (stopError is InvalidOperationException or Win32Exception)
            {
                // The process did not start or has already exited.
            }

            job?.Dispose();
            process.Dispose();
            throw new IOException($"Could not start {launch.Shell.Name}: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<int> WaitAsync(CancellationToken cancellationToken)
    {
        await _process.WaitForExitAsync(cancellationToken);
        Kill();
        return _process.ExitCode;
    }

    /// <inheritdoc />
    public void Kill()
    {
        if (OperatingSystem.IsWindows())
        {
            _job.Kill();
        }

        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // The process had already exited.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            _job.Dispose();
        }

        _process.Dispose();
    }
}
