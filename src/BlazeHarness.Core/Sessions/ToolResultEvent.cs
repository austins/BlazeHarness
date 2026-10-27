using BlazeHarness.Core.Agent;

namespace BlazeHarness.Core.Sessions;

/// <summary>
/// A tool call ended. A call with no result event was interrupted, for example by the app stopping.
/// </summary>
/// <param name="At">When the call ended.</param>
/// <param name="EntryId">The id of the reply that made the call.</param>
/// <param name="CallId">The provider's identifier for the call.</param>
/// <param name="Status">How the call ended.</param>
/// <param name="Output">The text of the result, or the reason the call did not run. When <paramref name="OutputFile"/> is set, only the start of it.</param>
/// <param name="OutputFile">The name of the file in the session's <c>tool-outputs</c> folder that holds the whole result, or null when the result is in <paramref name="Output"/>.</param>
internal sealed record ToolResultEvent(DateTimeOffset At, Guid EntryId, string CallId, ToolCallStatus Status, string Output, string? OutputFile = null) : SessionEvent(At);
