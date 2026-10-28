namespace BlazeHarness.Web.Hosting;

/// <summary>
/// Decides which <c>Host</c> headers <see cref="LocalAccessMiddleware"/> accepts.
/// </summary>
/// <param name="AllowAnyHost">
/// True when the app listens on all interfaces, so any IP address is accepted in the <c>Host</c> header (domain names are not,
/// which blocks DNS rebinding). False when it listens on loopback only, which accepts just <c>127.0.0.1</c> and <c>localhost</c>.
/// </param>
public sealed record AccessPolicy(bool AllowAnyHost);
