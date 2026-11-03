using BlazeHarness.Core.Tools.Files;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace BlazeHarness.Core.Sandbox.Ports;

/// <summary>
/// Forwards the ports of a chat's sandboxed background commands on Linux, where each command has a network of its own:
/// to localhost on this computer, so the browser tools reach them, and into the chat's other sandboxed commands.
/// </summary>
/// <remarks>
/// <para>
/// The background commands' networks are scanned for listening ports. For each new one, a <c>socat</c> relay in the
/// command's network listens on a Unix socket in a private folder of this app, and this app listens on the same port of
/// localhost and passes each connection to that socket. Every other sandboxed command of the chat gets relays in its
/// network that listen on the port and connect to the socket.
/// </para>
/// <para>
/// A command that starts while ports are forwarded first waits, for a few seconds at most, until its relays listen, so it
/// can connect at once.
/// </para>
/// </remarks>
internal sealed partial class SandboxPorts : IDisposable
{
    private const int GateChecks = 100;

    private static readonly TimeSpan ScanInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan FindWait = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ListenWait = TimeSpan.FromSeconds(2);

    private readonly Lock _gate = new();
    private readonly ForwardingTools _tools;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly List<SandboxMember> _members = [];
    private readonly Dictionary<int, PortRoute> _routes = [];
    private string? _directory;
    private ITimer? _timer;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SandboxPorts"/> class.
    /// </summary>
    /// <param name="tools">The programs that run the relays.</param>
    /// <param name="time">Times the scans and the waits.</param>
    /// <param name="logger">Logs the ports that are forwarded.</param>
    public SandboxPorts(ForwardingTools tools, TimeProvider time, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _tools = tools;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Get ready to start a command.
    /// </summary>
    /// <param name="launch">The command.</param>
    /// <param name="publishes">True for a background command, whose own ports are forwarded.</param>
    /// <returns>The command's part in the forwarding, or null when it runs outside the sandbox and needs none.</returns>
    public SandboxMember? Prepare(ShellLaunch launch, bool publishes)
    {
        ArgumentNullException.ThrowIfNull(launch);
        if (launch.Mode == ShellMode.Unconfined)
        {
            return null;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            string? gate = null;
            if (_routes.Count > 0)
            {
                Directory.CreateDirectory(launch.ScratchDirectory);
                gate = Path.Combine(RootedPath.CanonicalRoot(launch.ScratchDirectory), ".ports-" + Guid.NewGuid().ToString("N"));
                launch = launch with { Command = WaitFor(launch.Shell.IsPowerShell, gate) + launch.Command };
            }

            var member = new SandboxMember(this, launch, publishes, gate);
            _members.Add(member);
            return member;
        }
    }

    /// <summary>
    /// Stop all forwarding.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer?.Dispose();
            foreach (var member in _members)
            {
                CloseInbound(member);
            }

            foreach (var route in _routes.Values)
            {
                route.Dispose();
            }

            _members.Clear();
            _routes.Clear();
            if (_directory is not null)
            {
                TryDelete(_directory);
            }
        }
    }

    /// <summary>
    /// Look for new and closed ports now, as the timer does.
    /// </summary>
    internal void Scan()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            HashSet<int> open = [];
            foreach (var owner in _members.Where(member => member.Publishes && member.NamespacePid is not null).ToList())
            {
                foreach (var listening in ListeningPorts.Read(owner.NamespacePid!.Value))
                {
                    if (owner.Inbound.ContainsKey(listening.Port) || owner.Failed.Contains(listening.Port))
                    {
                        continue;
                    }

                    if (_routes.TryGetValue(listening.Port, out var existing))
                    {
                        if (ReferenceEquals(existing.Owner, owner))
                        {
                            open.Add(listening.Port);
                        }

                        continue;
                    }

#pragma warning disable CA2000, IDISP001 // The route goes into _routes, and CloseRoute disposes it.
                    var route = OpenRoute(owner, listening);
#pragma warning restore CA2000, IDISP001
                    if (route is null)
                    {
                        owner.Failed.Add(listening.Port);
                    }
                    else
                    {
                        _routes.Add(route.Port, route);
                        open.Add(route.Port);
                        foreach (var member in _members.Where(member => !ReferenceEquals(member, owner) && member.NamespacePid is not null))
                        {
                            OpenInbound(member, route);
                        }
                    }
                }
            }

            foreach (var closed in _routes.Keys.Where(port => !open.Contains(port)).ToList())
            {
                CloseRoute(closed);
            }
        }
    }

    internal async Task AttachAsync(SandboxMember member, IShellProcess process, CancellationToken cancellationToken)
    {
        if (process is not MxcShellProcess sandboxed)
        {
            OpenGate(member);
            return;
        }

        // A command that doesn't wait has nothing to wait for: connect it on the side.
        var attach = ConnectAsync(member, sandboxed.HostProcessId, cancellationToken);
        if (member.Gate is not null)
        {
            await attach;
        }
    }

    internal void Leave(SandboxMember member)
    {
        lock (_gate)
        {
            if (!_members.Remove(member))
            {
                return;
            }

            CloseInbound(member);
            foreach (var port in _routes.Values.Where(route => ReferenceEquals(route.Owner, member)).Select(route => route.Port).ToList())
            {
                CloseRoute(port);
            }

            if (!_members.Any(other => other.Publishes))
            {
                _timer?.Dispose();
                _timer = null;
            }
        }

        if (member.Gate is { } gate)
        {
            TryDeleteFile(gate);
        }
    }

    /// <summary>
    /// Get a command's ports that are forwarded.
    /// </summary>
    /// <param name="member">The command.</param>
    /// <returns>The ports, in order.</returns>
    internal IReadOnlyList<ForwardedPort> PortsOf(SandboxMember member)
    {
        lock (_gate)
        {
            return [.. _routes.Values.Where(route => ReferenceEquals(route.Owner, member)).OrderBy(route => route.Port).Select(route => new ForwardedPort(route.Port, route.Host is not null))];
        }
    }

    private void ScanSafely()
    {
        try
        {
            Scan();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogScanFailed(ex);
        }
    }

    private static string WaitFor(bool powerShell, string gate)
    {
        var count = GateChecks.ToString(CultureInfo.InvariantCulture);
        if (powerShell)
        {
            var quoted = gate.Replace("'", "''", StringComparison.Ordinal);
            return $"$__bh_n = 0; while (-not (Test-Path -LiteralPath '{quoted}') -and $__bh_n -lt {count}) {{ Start-Sleep -Milliseconds 50; $__bh_n++ }}; Remove-Item -LiteralPath '{quoted}' -ErrorAction SilentlyContinue; Remove-Variable __bh_n\n";
        }

        var escaped = gate.Replace("'", "'\\''", StringComparison.Ordinal);
        return $"__bh_n=0; while [ ! -e '{escaped}' ] && [ \"$__bh_n\" -lt {count} ]; do sleep 0.05; __bh_n=$((__bh_n+1)); done; rm -f '{escaped}'; unset __bh_n\n";
    }

    private static void OpenGate(SandboxMember member)
    {
        if (member.Gate is { } gate)
        {
            try
            {
                File.WriteAllBytes(gate, []);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The command stops waiting on its own after a few seconds.
            }
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An empty folder left in the temporary directory is harmless.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An empty file left in the chat's scratch folder is harmless.
        }
    }

    private async Task ConnectAsync(SandboxMember member, int hostPid, CancellationToken cancellationToken)
    {
        try
        {
            var pid = await FindAsync(hostPid, cancellationToken);
            if (pid is null)
            {
                return;
            }

            List<int> ports;
            lock (_gate)
            {
                if (_disposed || !_members.Contains(member))
                {
                    return;
                }

                member.NamespacePid = pid;
                foreach (var route in _routes.Values)
                {
                    OpenInbound(member, route);
                }

                ports = [.. member.Inbound.Keys];
                if (member.Publishes && _timer is null)
                {
                    _timer = _time.CreateTimer(_ => ScanSafely(), null, ScanInterval, ScanInterval);
                }
            }

            await WaitListeningAsync(pid.Value, ports, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The call was stopped; the command is stopped with it.
        }
        finally
        {
            OpenGate(member);
        }
    }

    private async Task<int?> FindAsync(int hostPid, CancellationToken cancellationToken)
    {
        var deadline = _time.GetUtcNow() + FindWait;
        while (true)
        {
            if (NetworkNamespace.FindInside(hostPid) is { } pid)
            {
                return pid;
            }

            if (NetworkNamespace.HasEnded(hostPid) || _time.GetUtcNow() >= deadline)
            {
                return null;
            }

            await Task.Delay(Poll, _time, cancellationToken);
        }
    }

    private async Task WaitListeningAsync(int pid, List<int> ports, CancellationToken cancellationToken)
    {
        var deadline = _time.GetUtcNow() + ListenWait;
        while (ports.Count > 0 && _time.GetUtcNow() < deadline)
        {
            var listening = ListeningPorts.Read(pid).Select(port => port.Port).ToHashSet();
            if (ports.TrueForAll(listening.Contains))
            {
                return;
            }

            await Task.Delay(Poll, _time, cancellationToken);
        }
    }

    private PortRoute? OpenRoute(SandboxMember owner, ListeningPort listening)
    {
        _directory ??= CreateDirectory();
        var socket = Path.Combine(_directory, string.Create(CultureInfo.InvariantCulture, $"{listening.Port}.sock"));
        var target = listening.OverIpv4
            ? string.Create(CultureInfo.InvariantCulture, $"TCP4:127.0.0.1:{listening.Port}")
            : string.Create(CultureInfo.InvariantCulture, $"TCP6:[::1]:{listening.Port}");
#pragma warning disable CA2000, IDISP001 // The relays go to the returned route, which disposes them.
        var relay = NamespaceRelay.Start(_tools, owner.NamespacePid!.Value, $"UNIX-LISTEN:{socket},fork,mode=600,unlink-early", target);
        if (relay is null)
        {
            LogRelayFailed(listening.Port);
            return null;
        }

        var host = HostPortRelay.Open(listening.Port, socket, _time);
        LogForwarded(listening.Port, host is not null);
        return new PortRoute(listening.Port, owner, socket, relay, host);
#pragma warning restore CA2000, IDISP001
    }

    private void OpenInbound(SandboxMember member, PortRoute route)
    {
        if (member.Inbound.ContainsKey(route.Port))
        {
            return;
        }

        var pid = member.NamespacePid!.Value;
        var connect = "UNIX-CONNECT:" + route.SocketPath;
        List<NamespaceRelay> relays = [];
        foreach (var listen in (string[])[
            string.Create(CultureInfo.InvariantCulture, $"TCP4-LISTEN:{route.Port},bind=127.0.0.1,fork,reuseaddr"),
            string.Create(CultureInfo.InvariantCulture, $"TCP6-LISTEN:{route.Port},bind=[::1],ipv6only=1,fork,reuseaddr")])
        {
#pragma warning disable CA2000 // The relay goes into the member's Inbound, which CloseInbound and CloseRoute dispose.
            if (NamespaceRelay.Start(_tools, pid, listen, connect) is { } relay)
            {
                relays.Add(relay);
            }
#pragma warning restore CA2000
        }

        member.Inbound[route.Port] = relays;
    }

    private void CloseRoute(int port)
    {
        if (!_routes.Remove(port, out var route))
        {
            return;
        }

        foreach (var member in _members)
        {
            if (member.Inbound.Remove(port, out var relays))
            {
                relays.ForEach(relay => relay.Dispose());
            }
        }

        route.Dispose();
        LogClosed(port);
    }

    private static void CloseInbound(SandboxMember member)
    {
        foreach (var relay in member.Inbound.Values.SelectMany(relays => relays))
        {
            relay.Dispose();
        }

        member.Inbound.Clear();
    }

    private static string CreateDirectory()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Ports are forwarded only on Linux.");
        }

        // A Unix socket path must stay short, so use the user's runtime folder, or /tmp rather than a deep temporary one.
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
#pragma warning disable S5443 // The folder gets a random name and owner-only access, and is checked below.
        var parent = !string.IsNullOrEmpty(runtime) && Path.IsPathRooted(runtime) && runtime.Length <= 60 ? runtime : "/tmp";
#pragma warning restore S5443
        var directory = Path.Combine(parent, "blazeharness-ports-" + Guid.NewGuid().ToString("N"));
        const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        var info = Directory.CreateDirectory(directory, ownerOnly);
        if (info.LinkTarget is not null || info.UnixFileMode != ownerOnly)
        {
            throw new IOException("The folder for the forwarded ports is not private.");
        }

        return directory;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Forwarding port {Port} of a sandboxed command; on this computer: {OnHost}")]
    private partial void LogForwarded(int port, bool onHost);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopped forwarding port {Port}")]
    private partial void LogClosed(int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not look for the ports of sandboxed commands")]
    private partial void LogScanFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not start the relay for port {Port}")]
    private partial void LogRelayFailed(int port);
}
