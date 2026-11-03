namespace BlazeHarness.Core.Sandbox.Ports;

/// <summary>
/// A TCP port a server listens on for connections from its own computer.
/// </summary>
/// <param name="Port">The port number.</param>
/// <param name="OverIpv4">True when 127.0.0.1 reaches it; false when only ::1 does.</param>
internal readonly record struct ListeningPort(int Port, bool OverIpv4);
