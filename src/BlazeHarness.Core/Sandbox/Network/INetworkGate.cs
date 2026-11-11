namespace BlazeHarness.Core.Sandbox.Network;

/// <summary>
/// Decides which hosts a sandboxed command may connect to, which can mean asking the user.
/// </summary>
public interface INetworkGate
{
    /// <summary>
    /// Gets the hosts the gate has refused so far, each with the reason, so the command's output can say why a connection failed.
    /// </summary>
    public IReadOnlyList<string> Refusals { get; }

    /// <summary>
    /// Decide whether the command may connect to a host.
    /// </summary>
    /// <param name="host">The host name or address the command asked for, without a port.</param>
    /// <param name="cancellationToken">A token that is cancelled when the command ends.</param>
    /// <returns>Whether to let the connection go ahead.</returns>
    public Task<NetworkAccess> RequestAsync(string host, CancellationToken cancellationToken);
}
