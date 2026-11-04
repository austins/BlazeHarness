using BlazeHarness.Core.Tools.Shell;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace BlazeHarness.Core.Git;

/// <summary>
/// Runs the git program found on the path.
/// </summary>
/// <param name="time">The clock that times the run out.</param>
internal sealed class GitProcessRunner(TimeProvider time) : IGitRunner
{
    private const int MaxOutputBytes = 4 * 1024 * 1024;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    /// <inheritdoc />
    public async Task<GitResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);

        var filters = await RunCoreAsync(workingDirectory, ["config", "--null", "--name-only", "--get-regexp", @"^filter\..*\.(clean|process|required)$"], cancellationToken);
        if (filters.Truncated || (filters.ExitCode != 0 && filters.ExitCode != 1))
        {
            return GitResult.Unavailable;
        }

        List<string> safeArguments = [];
        foreach (var driver in filters.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(key => key[..key.LastIndexOf('.')]).Distinct(StringComparer.Ordinal))
        {
            foreach (var setting in (string[])["clean=", "process=", "required=false"])
            {
                safeArguments.Add("-c");
                safeArguments.Add(driver + "." + setting);
            }
        }

        return await RunCoreAsync(workingDirectory, [.. safeArguments, .. arguments], cancellationToken);
    }

    private async Task<GitResult> RunCoreAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        // Only a fully qualified entry of the path counts, so a program in the repository can't pose as git.
        if (ShellInfo.FindOnPath(OperatingSystem.IsWindows() ? "git.exe" : "git", Environment.GetEnvironmentVariable("PATH")) is not { } git)
        {
            return GitResult.Unavailable;
        }

        var start = new ProcessStartInfo(git)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        // A repository's own configuration is data: it must not make this look-only run start other programs, take
        // locks the agent's own git commands need, or ask a question nobody can answer.
        foreach (var argument in (string[])["--no-optional-locks", "--literal-pathspecs", "-c", "core.fsmonitor=false", "-c", "core.quotepath=false"])
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["LC_ALL"] = "C";

        using var timeout = new CancellationTokenSource(Timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        Process process;
        try
        {
            process = Process.Start(start) ?? throw new Win32Exception("git did not start.");
        }
        catch (Win32Exception)
        {
            return GitResult.Unavailable;
        }

        using (process)
        {
            try
            {
                process.StandardInput.Close();
                var errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null, linked.Token);
                var (output, truncated) = await ReadAsync(process.StandardOutput.BaseStream, linked.Token);
                await errors;
                await process.WaitForExitAsync(linked.Token);
                return new GitResult(process.ExitCode, output, truncated);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return GitResult.Unavailable;
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
        }
    }

    private static async Task<(string Text, bool Truncated)> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var kept = new MemoryStream();
        var buffer = new byte[16 * 1024];
        var truncated = false;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            var room = MaxOutputBytes - (int)kept.Length;
            if (room > 0)
            {
                await kept.WriteAsync(buffer.AsMemory(0, Math.Min(read, room)), cancellationToken);
            }

            truncated |= read > room;
        }

        return (Encoding.UTF8.GetString(kept.GetBuffer(), 0, (int)kept.Length), truncated);
    }
}
