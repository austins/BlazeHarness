using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Tools;
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
    private readonly Lazy<SandboxStatus> _status;

    /// <summary>
    /// Initializes a new instance of the <see cref="MxcShellSandbox"/> class.
    /// </summary>
    /// <param name="sensitive">Gets the folders that stay out of reach even when they are inside the working folder, read at each launch so a change applies at once.</param>
    public MxcShellSandbox(Func<SensitivePaths> sensitive)
    {
        ArgumentNullException.ThrowIfNull(sensitive);
        _sensitive = sensitive;
        _status = new Lazy<SandboxStatus>(Probe, LazyThreadSafetyMode.ExecutionAndPublication);
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

        if (!Status.IsAvailable)
        {
            throw new InvalidOperationException(Status.Detail);
        }

        return await SpawnAsync(launch, cancellationToken).ConfigureAwait(false);
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

        var folder = Path.Combine(Path.GetTempPath(), "blazeharness-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            var launch = new ShellLaunch(shell, "exit 0", folder, ShellMode.Writable, folder, ProbeTimeout);
            var result = MxcContainer.Run(BuildRequest(launch));
            return result is { ExitCode: 0, TimedOut: false }
                ? new SandboxStatus(true, BackendName(), $"Commands run in a {BackendName()} sandbox that can only change the working folder. The network is not restricted.")
                : SandboxStatus.Unavailable($"The {BackendName()} sandbox failed its self-test (exit code {result.ExitCode}). Commands can only run with the Unrestricted access level.");
        }
        catch (Exception ex) when (ex is IOException or MxcException or DllNotFoundException or BadImageFormatException or UnauthorizedAccessException)
        {
            return SandboxStatus.Unavailable($"The {BackendName()} sandbox can't start: {ex.Message} Commands can only run with the Unrestricted access level.");
        }
        finally
        {
            TryDelete(folder);
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

    private ContainerRequest BuildRequest(ShellLaunch launch)
    {
        var platform = SandboxPolicy.CurrentPlatform();
        Directory.CreateDirectory(Path.Combine(launch.ScratchDirectory, "home"));
        Directory.CreateDirectory(Path.Combine(launch.ScratchDirectory, "tmp"));
        var policy = SandboxPolicy.Create(launch, platform, HostEnvironment(), _sensitive().Folders);

        var filesystem = new FilesystemPolicy();
        filesystem.ReadwritePaths.AddRange(policy.ReadWrite);
        filesystem.ReadonlyPaths.AddRange(policy.ReadOnly);
        filesystem.DeniedPaths.AddRange(policy.Denied);
        return new ContainerRequest(policy.CommandLine)
        {
            Containment = ContainmentFor(platform),
            Filesystem = filesystem,
            WorkingDirectory = launch.WorkingDirectory,
            Environment = new Dictionary<string, string>(policy.Environment, StringComparer.Ordinal),
            Network = new NetworkPolicy { Egress = new NetworkEgressPolicy { Default = NetworkAction.Allow } },
            TimeoutMs = (uint)Math.Clamp(launch.Timeout.TotalMilliseconds + 5_000, 1, uint.MaxValue),
        };
    }

    private async Task<IShellProcess> SpawnAsync(ShellLaunch launch, CancellationToken cancellationToken)
    {
        var request = BuildRequest(launch);
        try
        {
            return new MxcShellProcess(await MxcContainer.SpawnAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false));
        }
        catch (MxcException ex)
        {
            throw new IOException($"The sandbox could not start the command ({ex.Code}): {ex.Message}", ex);
        }
    }
}
