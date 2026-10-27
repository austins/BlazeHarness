using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Sandbox;

/// <summary>
/// The folders, environment and command line a sandboxed command runs with.
/// </summary>
/// <remarks>
/// The filesystem rules are an allow-list: a command sees the system runtime folders and the folders its shell and
/// <c>PATH</c> programs live in (read only), its working folder (read-write, or read only for <see cref="ShellMode.ReadOnly"/>)
/// and its scratch folder (read-write). Nothing else is visible, so the user's home folder and its credentials are out of reach.
/// The environment is rebuilt from a short list of names, so secret variables never reach the command.
/// </remarks>
internal sealed class SandboxPolicy
{
    internal const string CommandVariable = "BLAZEHARNESS_COMMAND";

    private static readonly string[] LinuxRuntime = ["/usr", "/bin", "/sbin", "/lib", "/lib32", "/lib64", "/libx32", "/etc", "/opt", "/run/systemd/resolve"];
    private static readonly string[] MacRuntime = ["/usr", "/bin", "/sbin", "/etc", "/private/etc", "/System", "/Library", "/opt"];
    private static readonly string[] PosixNames = ["PATH", "LANG", "LANGUAGE", "LC_ALL", "LC_CTYPE", "LC_MESSAGES", "TZ", "DOTNET_ROOT"];
    private static readonly string[] WindowsRuntimeNames = ["SystemRoot", "ProgramFiles", "ProgramFiles(x86)", "ProgramData"];
    private static readonly string[] WindowsNames =
    [
        "PATH", "PATHEXT", "SystemRoot", "windir", "SystemDrive", "ComSpec", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "ProgramData",
        "NUMBER_OF_PROCESSORS", "PROCESSOR_ARCHITECTURE", "OS", "DOTNET_ROOT",
    ];

    private SandboxPolicy(List<string> readWrite, List<string> readOnly, List<string> denied, Dictionary<string, string> environment, string commandLine)
    {
        ReadWrite = readWrite;
        ReadOnly = readOnly;
        Denied = denied;
        Environment = environment;
        CommandLine = commandLine;
    }

    /// <summary>
    /// Gets the folders the command may change.
    /// </summary>
    public IReadOnlyList<string> ReadWrite { get; }

    /// <summary>
    /// Gets the folders the command may read.
    /// </summary>
    public IReadOnlyList<string> ReadOnly { get; }

    /// <summary>
    /// Gets the folders inside the allowed ones that stay out of reach.
    /// </summary>
    public IReadOnlyList<string> Denied { get; }

    /// <summary>
    /// Gets the environment variables of the command.
    /// </summary>
    public IReadOnlyDictionary<string, string> Environment { get; }

    /// <summary>
    /// Gets the command line the container runs: the shell, which then runs the command text from the environment.
    /// </summary>
    public string CommandLine { get; }

    /// <summary>
    /// Get the platform of the running process.
    /// </summary>
    /// <returns>The platform.</returns>
    public static SandboxPlatform CurrentPlatform()
    {
        return OperatingSystem.IsWindows() ? SandboxPlatform.Windows : OperatingSystem.IsMacOS() ? SandboxPlatform.MacOS : SandboxPlatform.Linux;
    }

    /// <summary>
    /// Work out the rules for one command.
    /// </summary>
    /// <param name="launch">The command.</param>
    /// <param name="platform">The platform to write the rules for.</param>
    /// <param name="host">The environment variables of the app.</param>
    /// <param name="protectedFolders">Folders that must stay out of reach even when they are inside an allowed one.</param>
    /// <returns>The rules.</returns>
    public static SandboxPolicy Create(
        ShellLaunch launch,
        SandboxPlatform platform,
        IReadOnlyDictionary<string, string> host,
        IEnumerable<string> protectedFolders)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(protectedFolders);

        var scratch = WorkspacePath.CanonicalRoot(launch.ScratchDirectory);
        var home = Path.Combine(scratch, "home");
        var temp = Path.Combine(scratch, "tmp");
        var workDirectory = WorkspacePath.CanonicalRoot(launch.WorkingDirectory);

        List<string> readWrite = [scratch];
        List<string> readOnly = [];
        if (launch.Mode == ShellMode.ReadOnly)
        {
            readOnly.Add(workDirectory);
        }
        else
        {
            readWrite.Add(workDirectory);
        }

        var approved = (launch.ReadFolders ?? []).Where(Directory.Exists).Select(WorkspacePath.CanonicalRoot);
        foreach (var path in approved.Concat(RuntimeFolders(platform, host)).Append(Path.GetDirectoryName(launch.Shell.Executable) ?? string.Empty).Concat(PathFolders(host)))
        {
            if (path.Length > 0 && Directory.Exists(path) && !IsCovered(path, readOnly) && !IsCovered(path, readWrite))
            {
                readOnly.Add(WorkspacePath.CanonicalRoot(path));
            }
        }

        var denied = protectedFolders
            .Where(folder => Directory.Exists(folder)
                && (WorkspacePath.IsInside(workDirectory, folder) || readOnly.Exists(allowed => WorkspacePath.IsInside(allowed, folder))))
            .Select(WorkspacePath.CanonicalRoot)
            .ToList();

        return new SandboxPolicy(readWrite, readOnly, denied, BuildEnvironment(platform, host, home, temp, launch.Command), QuotedCommandLine(launch.Shell, platform));
    }

    private static string[] RuntimeFolders(SandboxPlatform platform, IReadOnlyDictionary<string, string> host)
    {
        return platform switch
        {
            SandboxPlatform.Linux => LinuxRuntime,
            SandboxPlatform.MacOS => MacRuntime,
            _ => [.. WindowsRuntimeNames.Select(name => host.GetValueOrDefault(name, string.Empty))],
        };
    }

    private static IEnumerable<string> PathFolders(IReadOnlyDictionary<string, string> host)
    {
        return host.GetValueOrDefault("PATH", string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool IsCovered(string path, List<string> allowed) => allowed.Exists(root => WorkspacePath.IsInside(root, path));

    private static Dictionary<string, string> BuildEnvironment(SandboxPlatform platform, IReadOnlyDictionary<string, string> host, string home, string temp, string command)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in platform == SandboxPlatform.Windows ? WindowsNames : PosixNames)
        {
            if (host.TryGetValue(name, out var value) && value.Length > 0)
            {
                environment[name] = value;
            }
        }

        environment["HOME"] = home;
        environment["TMPDIR"] = temp;
        environment["TERM"] = "dumb";
        if (platform == SandboxPlatform.Windows)
        {
            environment["USERPROFILE"] = home;
            environment["APPDATA"] = Path.Combine(home, "AppData", "Roaming");
            environment["LOCALAPPDATA"] = Path.Combine(home, "AppData", "Local");
            environment["TEMP"] = temp;
            environment["TMP"] = temp;
        }

        environment[CommandVariable] = command;
        return environment;
    }

    private static string QuotedCommandLine(ShellInfo shell, SandboxPlatform platform)
    {
        return platform == SandboxPlatform.Windows
            ? $"\"{shell.Executable}\" -NoLogo -NoProfile -NonInteractive -Command Invoke-Expression $env:{CommandVariable}"
            : $"'{shell.Executable.Replace("'", "'\\''", StringComparison.Ordinal)}' -c 'eval \"${CommandVariable}\"'";
    }
}
