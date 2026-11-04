namespace BlazeHarness.Core.Sandbox.Network;

/// <summary>
/// Whether a sandboxed command may connect to a host.
/// </summary>
/// <param name="Allowed">True when the connection may go ahead.</param>
/// <param name="Reason">When the connection is refused, why, in a sentence the command's user and the model can read.</param>
public sealed record NetworkAccess(bool Allowed, string Reason)
{
    /// <summary>
    /// Gets an answer that lets the connection go ahead.
    /// </summary>
    public static NetworkAccess Allow { get; } = new(true, string.Empty);

    /// <summary>
    /// Get an answer that refuses the connection.
    /// </summary>
    /// <param name="reason">Why it is refused.</param>
    /// <returns>The answer.</returns>
    public static NetworkAccess Refuse(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new NetworkAccess(false, reason);
    }
}
