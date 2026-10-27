namespace BlazeHarness.Core.Sessions;

/// <summary>
/// The model asked for a tool call in a reply.
/// </summary>
/// <param name="At">When the call was requested.</param>
/// <param name="EntryId">The id of the reply that made the call.</param>
/// <param name="CallId">The provider's identifier for the call.</param>
/// <param name="Name">The name of the tool.</param>
/// <param name="ArgumentsJson">The call arguments as a JSON object string.</param>
/// <param name="Summary">A few words describing the call, or an empty string.</param>
public sealed record ToolCallEvent(DateTimeOffset At, Guid EntryId, string CallId, string Name, string ArgumentsJson, string Summary) : SessionEvent(At);
