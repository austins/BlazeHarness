namespace BlazeHarness.Core.Sessions;

/// <summary>
/// Where a session's events are saved and read back.
/// </summary>
public interface ISessionLog
{
    /// <summary>
    /// Append events to the end of the log.
    /// </summary>
    /// <param name="events">The events to write, in order.</param>
    /// <param name="cancellationToken">A token to cancel the wait or the write.</param>
    /// <returns>A task that completes once the events are saved.</returns>
    Task AppendAsync(IReadOnlyCollection<SessionEvent> events, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read every event in the log.
    /// </summary>
    /// <remarks>
    /// A line that is not a valid event, such as a truncated last line, is skipped.
    /// </remarks>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The events in the order they were written, or none when nothing was saved.</returns>
    Task<IReadOnlyList<SessionEvent>> ReadAsync(CancellationToken cancellationToken = default);
}
