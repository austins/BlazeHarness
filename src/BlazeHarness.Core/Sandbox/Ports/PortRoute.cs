namespace BlazeHarness.Core.Sandbox.Ports;

/// <summary>
/// One port of a background command and the relays that carry it out of its sandbox.
/// </summary>
/// <param name="Port">The port.</param>
/// <param name="Owner">The command that listens on it.</param>
/// <param name="SocketPath">The Unix socket that leads to it.</param>
/// <param name="Out">The relay in the command's network, from the socket to the port.</param>
/// <param name="Host">The relay from localhost on this computer to the socket, or null when the port is in use here.</param>
internal sealed record PortRoute(int Port, SandboxMember Owner, string SocketPath, NamespaceRelay Out, HostPortRelay? Host) : IDisposable
{
    /// <summary>
    /// Stop the relays.
    /// </summary>
    public void Dispose()
    {
#pragma warning disable IDISP007 // The route owns the relays it is created with.
        Host?.Dispose();
        Out.Dispose();
#pragma warning restore IDISP007
    }
}
