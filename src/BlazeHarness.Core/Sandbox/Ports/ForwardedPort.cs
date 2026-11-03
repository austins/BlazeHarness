namespace BlazeHarness.Core.Sandbox.Ports;

/// <summary>
/// A port of a sandboxed background command that is forwarded out of its sandbox.
/// </summary>
/// <param name="Port">The port number, which is the same everywhere it is forwarded to.</param>
/// <param name="OnHost">True when localhost on this computer reaches it; false when another program already uses the port here, so only the chat's other sandboxed commands reach it.</param>
internal readonly record struct ForwardedPort(int Port, bool OnHost);
