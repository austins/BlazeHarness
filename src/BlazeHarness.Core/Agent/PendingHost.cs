namespace BlazeHarness.Core.Agent;

/// <summary>
/// A host a running command wants to connect to, waiting for the user to allow or refuse it.
/// </summary>
/// <param name="Host">The host name or address.</param>
/// <param name="CanRemember">True when the user may allow it for the rest of the chat.</param>
public sealed record PendingHost(string Host, bool CanRemember);
