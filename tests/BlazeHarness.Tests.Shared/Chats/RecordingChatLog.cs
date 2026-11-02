using BlazeHarness.Core.Chats;

namespace BlazeHarness.Tests.Shared.Chats;

/// <summary>A chat log that keeps events in memory and can be told to fail.</summary>
public sealed class RecordingChatLog : IChatLog
{
    private readonly List<ChatEvent> _events = [];
    private readonly Lock _gate = new();

    /// <summary>Gets or sets an exception that every append throws, or null to save normally.</summary>
    public Exception? FailWith { get; set; }

    /// <summary>Gets a copy of the saved events.</summary>
    public IReadOnlyList<ChatEvent> Events
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
    public Task AppendAsync(IReadOnlyCollection<ChatEvent> events, CancellationToken cancellationToken = default)
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
    public Task<IReadOnlyList<ChatEvent>> ReadAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Events);
    }
}
