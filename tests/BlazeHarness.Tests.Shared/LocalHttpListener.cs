using System.Net;
using System.Net.Sockets;

namespace BlazeHarness.Tests.Shared;

/// <summary>Starts an <see cref="HttpListener"/> on a free loopback port.</summary>
/// <remarks>
/// <see cref="HttpListener"/> cannot bind port 0, so a free port is probed and then released before the listener
/// takes it. Another process or test can claim it in between; starting is retried on a new port when that happens.
/// </remarks>
public static class LocalHttpListener
{
    private const int Attempts = 20;

    /// <summary>Start a listener on a free port.</summary>
    /// <param name="hosts">The host names to listen on, such as <c>127.0.0.1</c>.</param>
    /// <returns>The started listener and the port it listens on. The caller closes the listener.</returns>
    /// <exception cref="InvalidOperationException">No free port could be bound.</exception>
    public static (HttpListener Listener, int Port) Start(params string[] hosts)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var port = FreePort();
            var listener = new HttpListener();
            foreach (var host in hosts)
            {
                listener.Prefixes.Add($"http://{host}:{port}/");
            }

            try
            {
                listener.Start();
                return (listener, port);
            }
            catch (HttpListenerException)
            {
                listener.Close();
            }
        }

        throw new InvalidOperationException($"No free port could be bound after {Attempts} attempts.");
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
