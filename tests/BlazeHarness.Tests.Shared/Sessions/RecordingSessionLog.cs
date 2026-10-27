using BlazeHarness.Core.Sessions;

namespace BlazeHarness.Tests.Shared.Sessions;

/// <summary>A session log that keeps events in memory and can be told to fail.</summary>
public sealed class RecordingSessionLog : ISessionLog
{
    private readonly List<SessionEvent> _events = [];
    private readonly Lock _gate = new();

    /// <summary>Gets or sets an exception that every append throws, or null to save normally.</summary>
    public Exception? FailWith { get; set; }

    /// <summary>Gets a copy of the saved events.</summary>
    public IReadOnlyList<SessionEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    /// <inheritdoc />
    public Task AppendAsync(IReadOnlyCollection<SessionEvent> events, CancellationToken cancellationToken = default)
    {
        if (FailWith is not null)
        {
            throw FailWith;
        }

        lock (_gate)
        {
            _events.AddRange(events);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionEvent>> ReadAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Events);
    }
}
