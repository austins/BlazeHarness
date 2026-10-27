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

    private DirectShellProcess(Process process)
    {
        _process = process;
    }

    public Stream Output => _process.StandardOutput.BaseStream;

    public Stream Error => _process.StandardError.BaseStream;

    public static DirectShellProcess Start(ShellLaunch launch)
    {
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
        foreach (var argument in launch.Shell.ArgumentsFor(launch.Command))
        {
            info.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = info };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            process.Dispose();
            throw new IOException($"Could not start {launch.Shell.Name}: {ex.Message}", ex);
        }

        process.StandardInput.Close();
        return new DirectShellProcess(process);
    }

    public async Task<int> WaitAsync(CancellationToken cancellationToken)
    {
        await _process.WaitForExitAsync(cancellationToken);
        return _process.ExitCode;
    }

    public void Kill()
    {
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // The process had already exited.
        }
    }

    public void Dispose() => _process.Dispose();
}
