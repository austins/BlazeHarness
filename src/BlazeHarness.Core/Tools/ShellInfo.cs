namespace BlazeHarness.Core.Tools;

/// <summary>
/// The shell the <c>shell</c> tool runs commands in.
/// </summary>
/// <param name="Name">The name used in the tool description and the system prompt, such as <c>bash</c> or <c>pwsh</c>.</param>
/// <param name="Executable">The path or file name of the program to start.</param>
/// <param name="IsPowerShell">True when the shell is PowerShell, which takes different arguments than a POSIX shell.</param>
public sealed record ShellInfo(string Name, string Executable, bool IsPowerShell)
{
    private static readonly string[] PosixFallbacks = ["/bin/bash", "/usr/bin/bash", "/bin/sh"];

    /// <summary>
    /// Pick the shell for an operating system.
    /// </summary>
    /// <remarks>
    /// Windows prefers <c>pwsh</c>, then <c>powershell.exe</c>. Linux and macOS prefer the user's <c>SHELL</c> when it is
    /// bash, zsh or sh, then bash, then sh.
    /// </remarks>
    /// <param name="isWindows">True to pick a Windows shell.</param>
    /// <param name="shellVariable">The value of the <c>SHELL</c> environment variable, when there is one.</param>
    /// <param name="pathVariable">The value of the <c>PATH</c> environment variable, when there is one.</param>
    /// <returns>The shell, or null when none could be found.</returns>
    public static ShellInfo? Detect(bool isWindows, string? shellVariable, string? pathVariable)
    {
        if (isWindows)
        {
            return FindOnPath("pwsh.exe", pathVariable) is { } pwsh ? new ShellInfo("pwsh", pwsh, IsPowerShell: true)
                : FindOnPath("powershell.exe", pathVariable) is { } legacy ? new ShellInfo("powershell", legacy, IsPowerShell: true)
                : null;
        }

        if (!string.IsNullOrWhiteSpace(shellVariable) && File.Exists(shellVariable))
        {
            var name = Path.GetFileName(shellVariable);
            if (name is "bash" or "zsh" or "sh")
            {
                return new ShellInfo(name, shellVariable, IsPowerShell: false);
            }
        }

        var fallback = PosixFallbacks.FirstOrDefault(File.Exists);
        return fallback is null ? null : new ShellInfo(Path.GetFileName(fallback), fallback, IsPowerShell: false);
    }

    /// <summary>
    /// Pick the shell for the current machine.
    /// </summary>
    /// <returns>The shell, or null when none could be found.</returns>
    public static ShellInfo? DetectCurrent()
    {
        return Detect(
            OperatingSystem.IsWindows(),
            Environment.GetEnvironmentVariable("SHELL"),
            Environment.GetEnvironmentVariable("PATH"));
    }

    /// <summary>
    /// Get the arguments that make the shell run one command and exit.
    /// </summary>
    /// <param name="command">The command text.</param>
    /// <returns>The arguments, in order.</returns>
    public IReadOnlyList<string> ArgumentsFor(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return IsPowerShell ? ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", command] : ["-c", command];
    }

    private static string? FindOnPath(string fileName, string? pathVariable)
    {
        return (pathVariable ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Path.IsPathFullyQualified)
            .Select(directory => Path.Combine(directory, fileName))
            .FirstOrDefault(File.Exists);
    }
}
