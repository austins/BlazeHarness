namespace BlazeHarness.Core.Sessions;

/// <summary>
/// A chat read from an exported file, before it is saved as a session.
/// </summary>
/// <param name="Info">The metadata to give the new session. Its id is empty.</param>
/// <param name="Events">The events to write to the new session's log.</param>
public sealed record ImportedSession(SessionInfo Info, IReadOnlyList<SessionEvent> Events);
