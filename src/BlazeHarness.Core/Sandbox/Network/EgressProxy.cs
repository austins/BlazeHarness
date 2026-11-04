using BlazeHarness.Core.Web;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BlazeHarness.Core.Sandbox.Network;

/// <summary>
/// A proxy on a loopback port that a sandboxed command reaches the network through, so the command can only connect to the hosts a gate allows.
/// </summary>
/// <remarks>
/// <para>
/// It serves <c>CONNECT</c>, which carries HTTPS and anything else a client tunnels, and plain HTTP requests. It never looks inside a tunnel, so it
/// sees host names and nothing else. The gate is asked before the name is looked up, so a refused host leaks nothing through DNS.
/// </para>
/// <para>
/// It connects to the address the name resolved to, and only when that address is on the public internet, so an allowed name can't lead to this
/// computer or the local network.
/// </para>
/// </remarks>
internal sealed class EgressProxy : IDisposable
{
    private const int MaxHeadBytes = 16 * 1024;
    private static readonly TimeSpan HeadTimeout = TimeSpan.FromSeconds(30);
    private static readonly byte[] HeadEnd = "\r\n\r\n"u8.ToArray();

    private readonly INetworkGate _gate;
    private readonly Func<IPAddress, bool> _isAllowed;
    private readonly TimeProvider _time;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _lock = new();
    private readonly HashSet<Socket> _open = [];
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="EgressProxy"/> class and starts listening.
    /// </summary>
    /// <param name="gate">Decides which hosts may be reached.</param>
    /// <param name="time">Times how long a client may take to send its request.</param>
    /// <param name="isAllowed">Decides which resolved addresses may be connected to; defaults to public addresses only.</param>
    public EgressProxy(INetworkGate gate, TimeProvider time, Func<IPAddress, bool>? isAllowed = null)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(time);
        _gate = gate;
        _time = time;
        _isAllowed = isAllowed ?? WebAddressPolicy.IsPublic;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptAsync();
    }

    /// <summary>
    /// Gets the loopback port the proxy listens on.
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// Stop listening and close every connection.
    /// </summary>
    public void Dispose()
    {
        List<Socket> sockets;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            sockets = [.. _open];
            _open.Clear();
        }

        _stop.Cancel();
        _listener.Dispose();
        foreach (var socket in sockets)
        {
            socket.Dispose();
        }

        _stop.Dispose();
    }

    private static async Task<byte[]?> ReadHeadAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxHeadBytes];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0)
            {
                return null;
            }

            length += read;
            if (buffer.AsSpan(0, length).IndexOf(HeadEnd) >= 0)
            {
                return buffer[..length];
            }
        }

        return null;
    }

    private static async Task RefuseAsync(NetworkStream client, int status, string reason, CancellationToken cancellationToken)
    {
        var text = status == 403 ? "Forbidden" : "Bad Request";
        var body = Encoding.UTF8.GetBytes(reason.ReplaceLineEndings(" "));
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {text}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await client.WriteAsync(head, cancellationToken);
        await client.WriteAsync(body, cancellationToken);
    }

    private static async Task PumpAsync(Stream from, Stream to, Socket toSocket, CancellationToken cancellationToken)
    {
        try
        {
            await from.CopyToAsync(to, cancellationToken);
            toSocket.Shutdown(SocketShutdown.Send);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // One side went away, which ends the relay.
        }
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            Socket socket;
            try
            {
                socket = await _listener.AcceptSocketAsync(_stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            if (!Track(socket))
            {
                return;
            }

            _ = HandleAsync(socket);
        }
    }

    private bool Track(Socket socket)
    {
        lock (_lock)
        {
            if (_disposed)
            {
#pragma warning disable IDISP007 // The proxy owns every socket it accepts.
                socket.Dispose();
#pragma warning restore IDISP007
                return false;
            }

            _open.Add(socket);
            return true;
        }
    }

    private void Forget(Socket socket)
    {
        lock (_lock)
        {
            _open.Remove(socket);
        }

#pragma warning disable IDISP007 // The proxy owns every socket it accepts.
        socket.Dispose();
#pragma warning restore IDISP007
    }

    private async Task HandleAsync(Socket socket)
    {
        try
        {
            using var stream = new NetworkStream(socket, ownsSocket: false);
            await ServeAsync(stream, _stop.Token);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // The client or the command went away.
        }
        finally
        {
            Forget(socket);
        }
    }

    private async Task ServeAsync(NetworkStream client, CancellationToken cancellationToken)
    {
        byte[]? raw;
        using (var timeout = new CancellationTokenSource(HeadTimeout, _time))
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
        {
            raw = await ReadHeadAsync(client, linked.Token);
        }

        var end = raw is null ? -1 : raw.AsSpan().IndexOf(HeadEnd);
        if (raw is null || ProxyRequestHead.TryParse(Encoding.Latin1.GetString(raw, 0, end)) is not { } request)
        {
            await RefuseAsync(client, 400, "The network filter only serves HTTP proxy requests.", cancellationToken);
            return;
        }

        var access = await _gate.RequestAsync(request.Host, cancellationToken);
        if (!access.Allowed)
        {
            await RefuseAsync(client, 403, $"Blocked by the network filter: {access.Reason}", cancellationToken);
            return;
        }

        using var upstream = await ConnectAsync(request, cancellationToken);
        if (upstream is null)
        {
            await RefuseAsync(client, 403, $"Blocked by the network filter: {request.Host} is not a public internet address, or could not be reached.", cancellationToken);
            return;
        }

        Track(upstream);
        try
        {
            using var upstreamStream = new NetworkStream(upstream, ownsSocket: false);
            if (request.IsTunnel)
            {
                await client.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), cancellationToken);
                var early = raw.AsMemory(end + HeadEnd.Length);
                if (!early.IsEmpty)
                {
                    await upstreamStream.WriteAsync(early, cancellationToken);
                }
            }
            else
            {
                await upstreamStream.WriteAsync(request.Forward, cancellationToken);
                var body = raw.AsMemory(end + HeadEnd.Length);
                if (!body.IsEmpty)
                {
                    await upstreamStream.WriteAsync(body, cancellationToken);
                }
            }

            await Task.WhenAll(
                PumpAsync(client, upstreamStream, upstream, cancellationToken),
                PumpAsync(upstreamStream, client, client.Socket, cancellationToken));
        }
        finally
        {
            lock (_lock)
            {
                _open.Remove(upstream);
            }
        }
    }

    private async Task<Socket?> ConnectAsync(ProxyRequestHead request, CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(request.Host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(request.Host, cancellationToken);
        }
        catch (SocketException)
        {
            return null;
        }

        // Only an address that passes the check is connected to, so there is no gap between checking a name and using it.
        foreach (var address in addresses.Where(_isAllowed))
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(address, request.Port, cancellationToken);
#pragma warning disable IDISP011 // False positive: only the failure paths dispose the socket.
                return socket;
#pragma warning restore IDISP011
            }
            catch (SocketException)
            {
                socket.Dispose();
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        return null;
    }
}
