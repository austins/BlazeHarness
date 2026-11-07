using BlazeHarness.Core.Tools.Shell;
using System.ComponentModel;
using System.Diagnostics;

namespace BlazeHarness.Core.Tests.Integration.Mcp;

internal static class TestPython
{
    /// <summary>Gets the name of a Python program that runs, or null when there is none; the skip condition for tests that need Python.</summary>
    public static string? Command { get; } = Find();

    /// <summary>Gets a value indicating whether Python is installed.</summary>
    public static bool IsAvailable => Command is not null;

    /// <summary>
    /// Find a Python program by running it: Windows has a <c>python</c> and a <c>python3</c> Store alias that is on PATH
    /// without Python behind it, and that exits with an error.
    /// </summary>
    /// <returns>The program name, or null when none runs.</returns>
    private static string? Find()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        foreach (var name in new[] { "python3", "python" })
        {
            if (ShellInfo.FindOnPath(OperatingSystem.IsWindows() ? name + ".exe" : name, path) is { } program && Runs(program))
            {
                return name;
            }
        }

        return null;
    }

    private static bool Runs(string program)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(program, "--version") { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true });
            if (process is null)
            {
                return false;
            }

            if (process.WaitForExit(TimeSpan.FromSeconds(10)))
            {
                return process.ExitCode == 0;
            }

            process.Kill(entireProcessTree: true);
            return false;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
