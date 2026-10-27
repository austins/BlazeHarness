namespace BlazeHarness.Core.Sandbox;

/// <summary>
/// A command that has been started.
/// </summary>
public interface IShellProcess : IDisposable
{
    /// <summary>
    /// Gets the stream of what the command prints.
    /// </summary>
    Stream Output { get; }

    /// <summary>
    /// Gets the stream of what the command prints as errors.
    /// </summary>
    Stream Error { get; }

    /// <summary>
    /// Wait for the command to end.
    /// </summary>
    /// <param name="cancellationToken">Stops the wait, not the command.</param>
    /// <returns>The exit code.</returns>
    Task<int> WaitAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stop the command and everything it started.
    /// </summary>
    void Kill();
}
