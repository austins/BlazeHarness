using System.Diagnostics;
using System.Text;

namespace BlazeHarness.Core.Secrets;

/// <summary>Runs a short-lived helper process, passing input over stdin so secrets never appear in arguments.</summary>
internal static class ProcessRunner
{
    /// <summary>
    /// Run a process to completion, killing it and its children when the token is cancelled.
    /// </summary>
    /// <param name="fileName">The executable.</param>
    /// <param name="arguments">The arguments, passed without a shell.</param>
    /// <param name="stdin">Text to write to the process's stdin, or null.</param>
    /// <param name="cancellationToken">A token to cancel the run.</param>
    /// <returns>The exit code and captured output.</returns>
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? stdin,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        await using var killOnCancel = cancellationToken.Register(static state => Kill((Process)state!), process);

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin.AsMemory(), cancellationToken);
        }

        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellationToken);
        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }
}
