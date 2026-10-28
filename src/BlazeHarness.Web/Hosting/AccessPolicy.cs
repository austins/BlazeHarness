namespace BlazeHarness.Web.Hosting;

/// <summary>
/// Decides which <c>Host</c> headers <see cref="LocalAccessMiddleware"/> accepts.
/// </summary>
/// <param name="AllowAnyHost">
/// True when the app listens on all interfaces, so any IP address is accepted in the <c>Host</c> header (domain names are not,
/// which blocks DNS rebinding), plus <see cref="MachineName"/> with or without a private-network suffix. False when it listens on loopback only, which accepts just <c>127.0.0.1</c> and <c>localhost</c>.
/// </param>
public sealed record AccessPolicy(bool AllowAnyHost)
{
    /// <summary>
    /// Gets the host name of this machine, or an empty string for none. When <see cref="AllowAnyHost"/> is true it is accepted on its own
    /// and followed by a private-network suffix such as <c>.lan</c> or <c>.local</c>, which cannot be registered publicly.
    /// </summary>
    public string MachineName { get; init; } = string.Empty;
}
