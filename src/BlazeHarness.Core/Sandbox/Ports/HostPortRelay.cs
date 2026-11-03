using System.Net;
using System.Net.Sockets;

namespace BlazeHarness.Core.Sandbox.Ports;

/// <summary>
/// Listens on a localhost port of this computer and relays each connection to a Unix socket that leads into a sandbox.
/// </summary>
internal sealed class HostPortRelay : IDisposable
{
    private const int BufferSize = 16 * 1024;
    private const int ConnectAttempts = 40;

    private static readonly TimeSpan ConnectRetry = TimeSpan.FromMilliseconds(50);

    private readonly List<TcpListener> _listeners;
    private readonly string _socketPath;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _stop = new();

    private HostPortRelay(List<TcpListener> listeners, string socketPath, TimeProvider time)
    {
        _listeners = listeners;
        _socketPath = socketPath;
        _time = time;
        foreach (var listener in listeners)
        {
            _ = AcceptAsync(listener);
        }
    }

    /// <summary>
    /// Start listening.
    /// </summary>
    /// <param name="port">The port, on 127.0.0.1 and, when it is free there too, ::1.</param>
    /// <param name="socketPath">The Unix socket each connection goes to.</param>
    /// <param name="time">Times the retries while the socket is being created.</param>
    /// <returns>The relay, or null when another program already uses the port on 127.0.0.1.</returns>
    public static HostPortRelay? Open(int port, string socketPath, TimeProvider time)
    {
#pragma warning disable CA2000, IDISP001 // TryListen never throws, and the listeners go to the relay, which disposes them.
        var v4 = TryListen(IPAddress.Loopback, port);
        if (v4 is null)
        {
            return null;
        }

        List<TcpListener> listeners = [v4];
        if (Socket.OSSupportsIPv6 && TryListen(IPAddress.IPv6Loopback, port) is { } v6)
        {
            listeners.Add(v6);
        }

        return new HostPortRelay(listeners, socketPath, time);
#pragma warning restore CA2000, IDISP001
    }

    /// <summary>
    /// Stop listening and end the connections.
    /// </summary>
    public void Dispose()
    {
        _stop.Cancel();
        foreach (var listener in _listeners)
        {
            listener.Dispose();
        }

        _stop.Dispose();
    }

    private static TcpListener? TryListen(IPAddress address, int port)
    {
        var listener = new TcpListener(address, port);
        try
        {
            listener.Start();
            return listener;
        }
        catch (SocketException)
        {
            listener.Dispose();
            return null;
        }
    }

    private static async Task PumpAsync(Socket from, Socket to, CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        try
        {
            int read;
            while ((read = await from.ReceiveAsync(buffer, SocketFlags.None, cancellationToken)) > 0)
            {
                await to.SendAsync(buffer.AsMemory(0, read), SocketFlags.None, cancellationToken);
            }

            to.Shutdown(SocketShutdown.Send);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // One side went away; end the other so its pump doesn't wait for ever.
            TryShutdown(from);
            TryShutdown(to);
        }
    }

    private static void TryShutdown(Socket socket)
    {
        try
        {
            socket.Shutdown(SocketShutdown.Both);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            // Already closed.
        }
    }

    private async Task AcceptAsync(TcpListener listener)
    {
        var stop = _stop.Token;
        while (!stop.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await listener.AcceptSocketAsync(stop);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or OperationCanceledException)
            {
                return;
            }

            _ = RelayAsync(client, stop);
        }
    }

    private async Task RelayAsync(Socket client, CancellationToken cancellationToken)
    {
#pragma warning disable IDISP007 // The accepted connection is handed to this method to own.
        using (client)
#pragma warning restore IDISP007
        {
            using var target = await ConnectAsync(cancellationToken);
            if (target is not null)
            {
                await Task.WhenAll(PumpAsync(client, target, cancellationToken), PumpAsync(target, client, cancellationToken));
            }
        }
    }

    private async Task<Socket?> ConnectAsync(CancellationToken cancellationToken)
    {
        // The relay in the sandbox creates the socket a moment after the port is found.
        for (var attempt = 0; attempt < ConnectAttempts; attempt++)
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath), cancellationToken);
#pragma warning disable IDISP011 // Disposed only when connecting failed, and then not returned.
                return socket;
#pragma warning restore IDISP011
            }
            catch (SocketException)
            {
                socket.Dispose();
            }
            catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException)
            {
                socket.Dispose();
                return null;
            }

            try
            {
                await Task.Delay(ConnectRetry, _time, cancellationToken);
            }
            catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException)
            {
                return null;
            }
        }

        return null;
    }
}
