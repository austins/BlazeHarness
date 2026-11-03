using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Tools.Files;
using BlazeHarness.Core.Tools.Shell;
using Microsoft.Mxc.Sdk.V1;
using System.Runtime.InteropServices;

namespace BlazeHarness.Core.Sandbox;

/// <summary>
/// Runs shell commands in an MXC container: bubblewrap on Linux, Seatbelt on macOS and a process container on Windows.
/// </summary>
/// <remarks>
/// The policy is set explicitly on every command and doesn't depend on MXC defaults (see <see cref="SandboxPolicy"/>).
/// The network is not restricted. Whether the backend works is found out once, by running a trivial command.
/// </remarks>
public sealed class MxcShellSandbox : IShellSandbox
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    private readonly Func<SensitivePaths> _sensitive;
    private readonly Func<IReadOnlyDictionary<string, string>> _hostEnvironment;
    private readonly Lazy<SandboxStatus> _status;
    private readonly Lazy<string?> _fileDenyProblem;

    /// <summary>
    /// Initializes a new instance of the <see cref="MxcShellSandbox"/> class.
    /// </summary>
    /// <param name="sensitive">Gets the folders that stay out of reach even when they are inside the working folder, read at each launch so a change applies at once.</param>
    /// <param name="hostEnvironment">Gets the environment variables the command's own environment is built from; defaults to the process environment.</param>
    public MxcShellSandbox(Func<SensitivePaths> sensitive, Func<IReadOnlyDictionary<string, string>>? hostEnvironment = null)
    {
        ArgumentNullException.ThrowIfNull(sensitive);
        _sensitive = sensitive;
        _hostEnvironment = hostEnvironment ?? HostEnvironment;
        _status = new Lazy<SandboxStatus>(Probe, LazyThreadSafetyMode.ExecutionAndPublication);
        _fileDenyProblem = new Lazy<string?>(ProbeFileDeny, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public SandboxStatus Status => _status.Value;

    /// <inheritdoc />
    public async Task<IShellProcess> StartAsync(ShellLaunch launch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(launch);
        if (launch.Mode == ShellMode.Unconfined)
        {
            return DirectShellProcess.Start(launch);
        }

        // The first probe runs a command for seconds; keep it off the caller's thread.
        var status = _status.IsValueCreated ? _status.Value : await Task.Run(() => _status.Value, cancellationToken);
        if (!status.IsAvailable)
        {
            throw new InvalidOperationException(status.Detail);
        }

        return await SpawnAsync(launch, cancellationToken);
    }

    private static string? UnsupportedReason()
    {
        var architecture = RuntimeInformation.ProcessArchitecture;
        if (OperatingSystem.IsMacOS() && architecture != Architecture.Arm64)
        {
            return "The sandbox needs an Apple silicon Mac. Commands can only run with the Unrestricted access level.";
        }

        return architecture is Architecture.X64 or Architecture.Arm64
            ? null
            : "The sandbox isn't available for this processor. Commands can only run with the Unrestricted access level.";
    }

    private static Dictionary<string, string> HostEnvironment()
    {
        var host = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                host[key] = value;
            }
        }

        return host;
    }

    private static Containment ContainmentFor(SandboxPlatform platform)
    {
        return platform switch
        {
            SandboxPlatform.Windows => new Containment.ProcessContainer(),
            SandboxPlatform.MacOS => new Containment.Seatbelt(),
            _ => new Containment.Bubblewrap(),
        };
    }

    private SandboxStatus Probe()
    {
        if (UnsupportedReason() is { } reason)
        {
            return SandboxStatus.Unavailable(reason);
        }

        var shell = ShellInfo.DetectCurrent();
        if (shell is null)
        {
            return SandboxStatus.Unavailable("No shell was found to run commands in.");
        }

        var id = Guid.NewGuid().ToString("N");
        var folder = Path.Combine(Path.GetTempPath(), "blazeharness-probe-" + id);
        var outside = Path.Combine(Path.GetTempPath(), "blazeharness-probe-outside-" + id);
        try
        {
            Directory.CreateDirectory(folder);
            var launch = new ShellLaunch(shell, ProbeCommand(shell, outside), folder, ShellMode.Writable, folder, ProbeTimeout);
            var result = MxcContainer.Run(BuildRequest(launch).Request);
            if (result is not { ExitCode: 0, TimedOut: false })
            {
                return SandboxStatus.Unavailable($"The {BackendName()} sandbox failed its self-test (exit code {result.ExitCode}). Commands can only run with the Unrestricted access level.");
            }

            // Running isn't enough: the sandbox is only worth trusting if a write outside the working folder didn't land.
            return File.Exists(outside)
                ? SandboxStatus.Unavailable($"The {BackendName()} sandbox did not keep a command out of a folder it should not change. Commands can only run with the Unrestricted access level.")
                : new SandboxStatus(true, BackendName(), $"Commands run in a {BackendName()} sandbox that can only change the working folder. The network is not restricted.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Any failure to set up the backend, such as a missing native library entry point, means no sandbox. Never let it escape as an exception.
            return SandboxStatus.Unavailable($"The {BackendName()} sandbox can't start: {ex.Message} Commands can only run with the Unrestricted access level.");
        }
        finally
        {
            TryDelete(folder);
            TryDeleteFile(outside);
        }
    }

    /// <summary>
    /// Find out once whether the backend really keeps a single file out of reach: some only accept denied folders, and a deny
    /// the backend accepted but didn't apply would leave a refused file readable.
    /// </summary>
    /// <returns>Why commands that need a file kept out of reach can't run, or null when they can.</returns>
    private string? ProbeFileDeny()
    {
        const string Secret = "blazeharness-probe-secret";
        var cannot = $"This command was not run: the {BackendName()} sandbox on this system can't keep a single file out of a command's reach, which a file path rule of the user's or a protected file needs. Say what you wanted to do; the user can narrow the rule or choose the Unrestricted access level.";
        if (ShellInfo.DetectCurrent() is not { } shell)
        {
            return cannot;
        }

        var folder = Path.Combine(Path.GetTempPath(), "blazeharness-probe-deny-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            folder = RootedPath.CanonicalRoot(folder);
            var secret = Path.Combine(folder, "secret.txt");
            File.WriteAllText(secret, Secret);
            var command = shell.IsPowerShell ? "Get-Content -Raw -Path secret.txt -ErrorAction SilentlyContinue; exit 0" : "cat secret.txt 2>/dev/null; exit 0";
            var launch = new ShellLaunch(shell, command, folder, ShellMode.Writable, folder, ProbeTimeout, DeniedPaths: [secret]);
            var result = MxcContainer.Run(BuildRequest(launch).Request);
            return result.TimedOut || result.Stdout.Contains(Secret, StringComparison.Ordinal) ? cannot : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A backend that rejects the request, rather than run it unprotected, can't keep the file out either.
            return cannot;
        }
        finally
        {
            TryDelete(folder);
        }
    }

    private static string ProbeCommand(ShellInfo shell, string outside)
    {
        return shell.IsPowerShell
            ? $"Set-Content -Path '{outside.Replace("'", "''", StringComparison.Ordinal)}' -Value x -ErrorAction SilentlyContinue; exit 0"
            : $"echo x > '{outside.Replace("'", "'\\''", StringComparison.Ordinal)}' 2>/dev/null; exit 0";
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A probe file left in the temp directory is harmless.
        }
    }

    private static string BackendName()
    {
        return OperatingSystem.IsWindows() ? "Windows process container" : OperatingSystem.IsMacOS() ? "Seatbelt" : "bubblewrap";
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A probe folder left in the temp directory is harmless.
        }
    }

    private (ContainerRequest Request, SandboxPolicy Policy) BuildRequest(ShellLaunch launch)
    {
        var platform = SandboxPolicy.CurrentPlatform();
        Directory.CreateDirectory(Path.Combine(launch.ScratchDirectory, "home"));
        Directory.CreateDirectory(Path.Combine(launch.ScratchDirectory, "tmp"));
        var policy = SandboxPolicy.Create(launch, platform, _hostEnvironment(), _sensitive().Folders);

        var filesystem = new FilesystemPolicy();
        filesystem.ReadwritePaths.AddRange(policy.ReadWrite);
        filesystem.ReadonlyPaths.AddRange(policy.ReadOnly);
        filesystem.DeniedPaths.AddRange(policy.Denied);
        var request = new ContainerRequest(policy.CommandLine)
        {
            Containment = ContainmentFor(platform),
            Filesystem = filesystem,
            WorkingDirectory = launch.WorkingDirectory,
            Environment = new Dictionary<string, string>(policy.Environment, StringComparer.Ordinal),
            Network = new NetworkPolicy { Egress = new NetworkEgressPolicy { Default = NetworkAction.Allow } },
            TimeoutMs = (uint)Math.Clamp(launch.Timeout.TotalMilliseconds + 5_000, 1, uint.MaxValue),
        };
        return (request, policy);
    }

    private async Task<IShellProcess> SpawnAsync(ShellLaunch launch, CancellationToken cancellationToken)
    {
        var (request, policy) = BuildRequest(launch);
        if (policy.Denied.Any(File.Exists) && (_fileDenyProblem.IsValueCreated ? _fileDenyProblem.Value : await Task.Run(() => _fileDenyProblem.Value, cancellationToken)) is { } problem)
        {
            throw new IOException(problem);
        }

        try
        {
            return new MxcShellProcess(await MxcContainer.SpawnAsync(request, cancellationToken: cancellationToken));
        }
        catch (MxcException ex)
        {
            var hint = policy.Denied.Count > 0 ? " It may not be able to keep files out of reach on this system, which a rule of the user's or a protected path needs." : string.Empty;
            throw new IOException($"The sandbox could not start the command ({ex.Code}): {ex.Message}{hint}", ex);
        }
    }
}
