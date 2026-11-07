using BlazeHarness.Core.Sandbox;

namespace BlazeHarness.Web.Tests.E2E;

/// <summary>
/// A sandbox that reports itself unavailable at once. The real one starts an OS container to probe itself, which is slow
/// on Windows and, with every parallel test hosting its own app, holds up page loads that read the status.
/// </summary>
internal sealed class UnavailableShellSandbox : IShellSandbox
{
    /// <inheritdoc />
    public SandboxStatus Status { get; } = SandboxStatus.Unavailable("The sandbox is off in UI tests.");

    /// <inheritdoc />
    public Task<IShellProcess> StartAsync(ShellLaunch launch, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(Status.Detail);
}
