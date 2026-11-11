using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace BlazeHarness.Core.Sandbox.Ports;

/// <summary>
/// A <c>socat</c> relay running in a sandbox's network, started from outside the sandbox with <c>nsenter</c>.
/// </summary>
/// <remarks>
/// It keeps this app's file system, so a Unix socket path it uses is a path on this computer, out of the sandbox's reach.
/// </remarks>
internal sealed class NamespaceRelay : IDisposable
{
    private readonly Process _process;

    private NamespaceRelay(Process process)
    {
        _process = process;
    }

    /// <summary>
    /// Start a relay.
    /// </summary>
    /// <param name="tools">The programs.</param>
    /// <param name="pid">A process in the sandbox's network.</param>
    /// <param name="listen">The <c>socat</c> address to listen on.</param>
    /// <param name="connect">The <c>socat</c> address each connection goes to.</param>
    /// <returns>The relay, or null when it could not start.</returns>
    public static NamespaceRelay? Start(ForwardingTools tools, int pid, string listen, string connect)
    {
        using var nullHandle = File.OpenNullHandle();
        var info = new ProcessStartInfo(tools.Nsenter)
        {
            UseShellExecute = false,
            StandardInputHandle = nullHandle,
            StandardOutputHandle = nullHandle,
            StandardErrorHandle = nullHandle,
        };

        // A relay left behind by a crash would keep its socket and port; the parent-death signal survives nsenter.
        if (OperatingSystem.IsLinux())
        {
            info.KillOnParentExit = true;
        }

        // Entering the user namespace too is what lets an unprivileged process join the network one.
        foreach (var argument in (string[])["-t", pid.ToString(CultureInfo.InvariantCulture), "-U", "-n", "--preserve-credentials", tools.Socat, listen, connect])
        {
            info.ArgumentList.Add(argument);
        }

        Process? process = null;
        try
        {
            process = Process.Start(info);
            if (process is null)
            {
                return null;
            }

            var relay = new NamespaceRelay(process);
#pragma warning disable IDISP003 // The relay now owns the process; clearing the local keeps the finally from disposing it.
            process = null;
#pragma warning restore IDISP003
            return relay;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
        finally
        {
            process?.Dispose();
        }
    }

    /// <summary>
    /// Stop the relay and the connections it is relaying.
    /// </summary>
    public void Dispose()
    {
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // It had already ended.
        }

        _process.Dispose();
    }
}
