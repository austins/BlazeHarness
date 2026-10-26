using System.Diagnostics;
using System.Text;

namespace BlazeHarness.Core.Secrets;

internal sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);

/// <summary>Runs a short-lived helper process, passing input over stdin so secrets never appear in arguments.</summary>
internal static class ProcessRunner
{
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
}
