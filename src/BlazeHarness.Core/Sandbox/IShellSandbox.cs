namespace BlazeHarness.Core.Sandbox;

/// <summary>
/// Starts shell commands, inside an OS sandbox unless the call asks for none.
/// </summary>
public interface IShellSandbox
{
    /// <summary>
    /// Gets whether commands can be confined on this computer.
    /// </summary>
    SandboxStatus Status { get; }

    /// <summary>
    /// Start a command.
    /// </summary>
    /// <param name="launch">The command and how far it may reach.</param>
    /// <param name="cancellationToken">Stops the start.</param>
    /// <returns>The running command.</returns>
    /// <exception cref="InvalidOperationException">The call needs a sandbox and <see cref="Status"/> says there is none.</exception>
    /// <exception cref="IOException">The command could not be started.</exception>
    Task<IShellProcess> StartAsync(ShellLaunch launch, CancellationToken cancellationToken);
}
