namespace BlazeHarness.Core.Sandbox;

/// <summary>
/// A sandbox that cannot confine anything, so only commands that ask for no sandbox can run.
/// </summary>
public sealed class NoShellSandbox : IShellSandbox
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoShellSandbox"/> class.
    /// </summary>
    /// <param name="reason">Why there is no sandbox, as a sentence for the user.</param>
    public NoShellSandbox(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Status = SandboxStatus.Unavailable(reason);
    }

    /// <inheritdoc />
    public SandboxStatus Status { get; }

    /// <inheritdoc />
    public Task<IShellProcess> StartAsync(ShellLaunch launch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(launch);
        return launch.Mode == ShellMode.Unconfined
            ? Task.FromResult<IShellProcess>(DirectShellProcess.Start(launch))
            : throw new InvalidOperationException(Status.Detail);
    }
}
