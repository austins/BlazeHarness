namespace BlazeHarness.Core.Sandbox;

/// <summary>
/// A command that has been started.
/// </summary>
public interface IShellProcess : IDisposable
{
    /// <summary>
    /// Gets the stream of what the command prints.
    /// </summary>
    public Stream Output { get; }

    /// <summary>
    /// Gets the stream of what the command prints as errors.
    /// </summary>
    public Stream Error { get; }

    /// <summary>
    /// Wait for the command to end.
    /// </summary>
    /// <param name="cancellationToken">Stops the wait, not the command.</param>
    /// <returns>The exit code.</returns>
    public Task<int> WaitAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stop the command and everything it started.
    /// </summary>
    public void Kill();
}
