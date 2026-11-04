using BlazeHarness.Core.Sandbox.Network;
using BlazeHarness.Tests.Shared;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BlazeHarness.Core.Tests.Integration.Sandbox.Network;

public sealed class EgressProxyTests : IDisposable
{
    private readonly INetworkGate _gate = Substitute.For<INetworkGate>();
    private readonly TcpListener _upstream = new(IPAddress.Loopback, 0);

    public EgressProxyTests()
    {
        _upstream.Start();
        _gate.RequestAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(NetworkAccess.Allow);
    }

    private int UpstreamPort => ((IPEndPoint)_upstream.LocalEndpoint).Port;

    public void Dispose() => _upstream.Dispose();

    [Fact]
    public async Task Connect_AllowedHost_TunnelsBytesBothWays()
    {
        // Arrange
        using var sut = new EgressProxy(_gate, TimeProvider.System, _ => true);
        var served = EchoOnceAsync();
        using var client = await ConnectAsync(sut);

        // Act
        var reply = await SendAsync(client, $"CONNECT localhost:{UpstreamPort} HTTP/1.1\r\nHost: localhost:{UpstreamPort}\r\n\r\n");
        await client.GetStream().WriteAsync("ping"u8.ToArray(), TestContext.Current.CancellationToken);
        var echoed = await ReadAllAsync(client);
        await served.Bounded();

        // Assert
        reply.Should().StartWith("HTTP/1.1 200");
        echoed.Should().Be("echo:ping");
        await _gate.Received(1).RequestAsync("localhost", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_AbsoluteUri_ForwardsInOriginFormAndReturnsTheResponse()
    {
        // Arrange
        using var sut = new EgressProxy(_gate, TimeProvider.System, _ => true);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var served = ServeAsync(async stream =>
        {
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);
            received.SetResult(Encoding.ASCII.GetString(buffer, 0, read));
            await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray(), TestContext.Current.CancellationToken);
        });
        using var client = await ConnectAsync(sut);

        // Act
        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes($"GET http://localhost:{UpstreamPort}/a?b=1 HTTP/1.1\r\nHost: localhost:{UpstreamPort}\r\nProxy-Connection: keep-alive\r\n\r\n"), TestContext.Current.CancellationToken);
        var response = await ReadAllAsync(client);
        await served.Bounded();

        // Assert
        response.Should().StartWith("HTTP/1.1 200 OK").And.EndWith("ok");
        var forwarded = await received.Task.Bounded();
        forwarded.Should().StartWith("GET /a?b=1 HTTP/1.1").And.NotContain("Proxy-Connection");
    }

    [Fact]
    public async Task Connect_RefusedByTheGate_Returns403WithoutConnecting()
    {
        // Arrange
        _gate.RequestAsync("localhost", Arg.Any<CancellationToken>()).Returns(NetworkAccess.Refuse("No."));
        using var sut = new EgressProxy(_gate, TimeProvider.System, _ => true);
        using var client = await ConnectAsync(sut);

        // Act
        var reply = await SendAsync(client, $"CONNECT localhost:{UpstreamPort} HTTP/1.1\r\n\r\n");

        // Assert
        reply.Should().StartWith("HTTP/1.1 403");
        _upstream.Pending().Should().BeFalse();
    }

    [Fact]
    public async Task Connect_AllowedHostOnANonPublicAddress_Returns403()
    {
        // Arrange
        using var sut = new EgressProxy(_gate, TimeProvider.System);
        using var client = await ConnectAsync(sut);

        // Act
        var reply = await SendAsync(client, $"CONNECT localhost:{UpstreamPort} HTTP/1.1\r\n\r\n");

        // Assert
        reply.Should().StartWith("HTTP/1.1 403");
        _upstream.Pending().Should().BeFalse();
    }

    [Fact]
    public async Task Request_Malformed_Returns400AndNeverAsksTheGate()
    {
        // Arrange
        using var sut = new EgressProxy(_gate, TimeProvider.System, _ => true);
        using var client = await ConnectAsync(sut);

        // Act
        var reply = await SendAsync(client, "garbage\r\n\r\n");

        // Assert
        reply.Should().StartWith("HTTP/1.1 400");
        await _gate.DidNotReceiveWithAnyArgs().RequestAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Dispose_ClosesOpenTunnels()
    {
        // Arrange
        using var sut = new EgressProxy(_gate, TimeProvider.System, _ => true);
        var served = ServeAsync(async stream => _ = await stream.ReadAsync(new byte[1], TestContext.Current.CancellationToken));
        using var client = await ConnectAsync(sut);
        (await SendAsync(client, $"CONNECT localhost:{UpstreamPort} HTTP/1.1\r\n\r\n")).Should().StartWith("HTTP/1.1 200");

        // Act
#pragma warning disable IDISP017 // Disposing early is what is under test; the using disposes again, which must be harmless.
        sut.Dispose();
#pragma warning restore IDISP017
        var rest = await ReadAllAsync(client);
        await served.Bounded();

        // Assert
        rest.Should().BeEmpty();
    }

    private static async Task<TcpClient> ConnectAsync(EgressProxy proxy)
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port, TestContext.Current.CancellationToken).AsTask().Bounded();
        return client;
    }

    // Reads the proxy's reply head, which ends at the blank line.
    private static async Task<string> SendAsync(TcpClient client, string request)
    {
#pragma warning disable IDISP001 // The stream belongs to the client, which the caller disposes.
        var stream = client.GetStream();
#pragma warning restore IDISP001
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), TestContext.Current.CancellationToken);
        var head = new StringBuilder();
        var one = new byte[1];
        while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            var read = await stream.ReadAsync(one, TestContext.Current.CancellationToken).AsTask().Bounded();
            if (read == 0)
            {
                break;
            }

            head.Append((char)one[0]);
        }

        return head.ToString();
    }

    private static async Task<string> ReadAllAsync(TcpClient client)
    {
        using var reader = new StreamReader(client.GetStream(), Encoding.ASCII);
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken).Bounded();
    }

    private async Task ServeAsync(Func<NetworkStream, Task> handle)
    {
        using var peer = await _upstream.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        await handle(peer.GetStream());
    }

    private Task EchoOnceAsync() => ServeAsync(async stream =>
    {
        var buffer = new byte[16];
        var read = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);
        await stream.WriteAsync(Encoding.ASCII.GetBytes("echo:" + Encoding.ASCII.GetString(buffer, 0, read)), TestContext.Current.CancellationToken);
    });
}
