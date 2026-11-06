using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Sandbox.Network;
using BlazeHarness.Core.Sandbox.Ports;
using BlazeHarness.Core.Tools.Files;
using BlazeHarness.Core.Tools.Shell;
using Microsoft.Mxc.Sdk.V1;
using System.Globalization;
using System.Runtime.InteropServices;

namespace BlazeHarness.Core.Sandbox;

/// <summary>
/// Runs shell commands in an MXC container: bubblewrap on Linux, Seatbelt on macOS and a process container on Windows.
/// </summary>
/// <remarks>
/// The policy is set explicitly on every command and doesn't depend on MXC defaults (see <see cref="SandboxPolicy"/>).
/// The network is open unless a command's launch carries a <see cref="INetworkGate"/>: then every connection goes through an
/// <see cref="EgressProxy"/> that asks the gate, and the sandbox blocks any other route out. Each command has its own network on Linux;
/// <see cref="SandboxPorts"/> forwards the ports of background commands. Whether the backend works is found out once, by running a trivial command.
/// </remarks>
public sealed class MxcShellSandbox : IShellSandbox
{
    /// <summary>
    /// The time a self-test command gets. The Windows container takes about 28 seconds to start a command on some Windows 11 builds (microsoft/mxc#1629).
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(OperatingSystem.IsWindows() ? 60 : 15);

    private readonly Func<SensitivePaths> _sensitive;
    private readonly Func<IReadOnlyDictionary<string, string>> _hostEnvironment;
    private readonly Lazy<SandboxStatus> _status;
    private readonly Lazy<string?> _fileDenyProblem;
    private readonly TimeProvider _time;

    /// <summary>
    /// Initializes a new instance of the <see cref="MxcShellSandbox"/> class.
    /// </summary>
    /// <param name="sensitive">Gets the folders that stay out of reach even when they are inside the working folder, read at each launch so a change applies at once.</param>
    /// <param name="hostEnvironment">Gets the environment variables the command's own environment is built from; defaults to the process environment.</param>
    /// <param name="time">Times the network filter's proxy; defaults to the system clock.</param>
    public MxcShellSandbox(Func<SensitivePaths> sensitive, Func<IReadOnlyDictionary<string, string>>? hostEnvironment = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(sensitive);
        _time = time ?? TimeProvider.System;
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
                return SandboxStatus.Unavailable($"The {BackendName()} sandbox failed its self-test (exit code {result.ExitCode}, 0x{result.ExitCode:X8}{(result.TimedOut ? ", timed out" : string.Empty)}). Commands can only run with the Unrestricted access level.");
            }

            // Running isn't enough: the sandbox is only worth trusting if a write outside the working folder didn't land.
            return File.Exists(outside)
                ? SandboxStatus.Unavailable($"The {BackendName()} sandbox did not keep a command out of a folder it should not change. Commands can only run with the Unrestricted access level.")
                : Available();
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

    private static SandboxStatus Available()
    {
        var detail = $"Commands run in a {BackendName()} sandbox that can only change the working folder. The network is open unless the host filter is turned on in Settings.";
        if (ForwardingTools.Find() is not null)
        {
            return new SandboxStatus(true, BackendName(), detail + " A server a background command starts is forwarded to localhost, where the browser tools and later commands reach it.", ForwardsPorts: true);
        }

        var missing = OperatingSystem.IsLinux() ? " Install socat and nsenter (util-linux) to have them forwarded." : string.Empty;
        return new SandboxStatus(true, BackendName(), detail + $" A server a command starts can only be reached from inside that command, so the browser tools need the Unrestricted access level.{missing}");
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

    private (ContainerRequest Request, SandboxPolicy Policy) BuildRequest(ShellLaunch launch, int? proxyPort = null)
    {
        var platform = SandboxPolicy.CurrentPlatform();
        Directory.CreateDirectory(Path.Combine(launch.ScratchDirectory, "home"));
        Directory.CreateDirectory(Path.Combine(launch.ScratchDirectory, "tmp"));
        if (proxyPort is not null)
        {
            launch = launch with { Command = NoProxyPrefix(launch.Shell) + launch.Command };
        }

        var policy = SandboxPolicy.Create(launch, platform, _hostEnvironment(), _sensitive().Folders);

        var filesystem = new FilesystemPolicy();
        filesystem.ReadwritePaths.AddRange(policy.ReadWrite);
        filesystem.ReadonlyPaths.AddRange(policy.ReadOnly);
        filesystem.DeniedPaths.AddRange(policy.Denied);
        var request = new ContainerRequest(policy.CommandLine)
        {
            Containment = ContainmentFor(platform),
            Filesystem = filesystem,
            WorkingDirectory = RootedPath.CanonicalRoot(launch.WorkingDirectory),
            Environment = new Dictionary<string, string>(policy.Environment, StringComparer.Ordinal),
            Network = proxyPort is { } port ? FilteredNetwork(port) : new NetworkPolicy { Egress = new NetworkEgressPolicy { Default = NetworkAction.Allow } },
            TimeoutMs = (uint)Math.Clamp(launch.Timeout.TotalMilliseconds + 5_000, 1, uint.MaxValue),
        };
        if (platform == SandboxPlatform.Windows)
        {
            // With UI disabled the Windows container blocks the Win32k calls that PowerShell needs to start (exit code 0xC0000142 on Windows 11).
            // The clipboard and input injection stay off.
            request.Ui = new UiPolicy { Disable = false };
        }

        return (request, policy);
    }

    private static NetworkPolicy FilteredNetwork(int port)
    {
        // The only way out is the proxy on this computer's loopback; the backend blocks every other route, including the loopback itself.
        return new NetworkPolicy
        {
            Egress = new NetworkEgressPolicy { Default = NetworkAction.Deny },
            Ingress = new NetworkIngressPolicy { Default = NetworkAction.Deny, HostLoopback = NetworkAction.Deny },
            RuntimeConfig = new NetworkRuntimeConfig { NetworkProxy = ProxyUrl(port) },
        };
    }

    /// <summary>
    /// Build the loopback URL of the egress proxy.
    /// </summary>
    /// <remarks>The backend also sets HTTP_PROXY, HTTPS_PROXY, ALL_PROXY and their lowercase forms from this URL.</remarks>
    /// <param name="port">The proxy's listening port.</param>
    private static string ProxyUrl(int port) => string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port}");

    /// <summary>
    /// Build the shell prefix that bypasses the proxy for loopback hosts.
    /// </summary>
    /// <remarks>
    /// The backend drops NO_PROXY from its environment, so the command sets it itself.
    /// A server started on the command's own loopback on Linux needs no proxy.
    /// macOS shares the host network, so its backend blocks those connections along with host-local services.
    /// </remarks>
    /// <param name="shell">The shell whose environment syntax to use.</param>
    private static string NoProxyPrefix(ShellInfo shell)
    {
        const string Hosts = "localhost,127.0.0.1,::1";
        return shell.IsPowerShell
            ? $"$env:NO_PROXY='{Hosts}'; $env:no_proxy='{Hosts}'\n"
            : $"export NO_PROXY='{Hosts}' no_proxy='{Hosts}'\n";
    }

    private async Task<IShellProcess> SpawnAsync(ShellLaunch launch, CancellationToken cancellationToken)
    {
        if (launch.Network is not null && OperatingSystem.IsWindows())
        {
            throw new IOException("This command was not run: the network filter isn't supported by the Windows sandbox yet. The user can turn it off in Settings.");
        }

        var proxy = launch.Network is { } gate ? new EgressProxy(gate, _time) : null;
        try
        {
            var (request, policy) = BuildRequest(launch, proxy?.Port);
            if (policy.Denied.Any(File.Exists) && (_fileDenyProblem.IsValueCreated ? _fileDenyProblem.Value : await Task.Run(() => _fileDenyProblem.Value, cancellationToken)) is { } problem)
            {
                throw new IOException(problem);
            }

            return new MxcShellProcess(await MxcContainer.SpawnAsync(request, cancellationToken: cancellationToken), proxy);
        }
        catch (MxcException ex)
        {
            proxy?.Dispose();
            var hint = proxy is null ? string.Empty : " The network filter needs slirp4netns, iptables and nsenter on Linux; the user can turn it off in Settings.";
            throw new IOException($"The sandbox could not start the command ({ex.Code}): {ex.Message}{hint}", ex);
        }
        catch
        {
            proxy?.Dispose();
            throw;
        }
    }
}
