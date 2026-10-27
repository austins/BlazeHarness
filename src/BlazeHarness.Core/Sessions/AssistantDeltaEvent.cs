namespace BlazeHarness.Core.Sessions;

/// <summary>
/// A batch of streamed reply text.
/// </summary>
/// <param name="At">When the batch was written.</param>
/// <param name="EntryId">The id of the reply the text belongs to.</param>
/// <param name="Text">Visible text to append, or an empty string.</param>
/// <param name="Reasoning">Thinking text to append, or an empty string.</param>
internal sealed record AssistantDeltaEvent(DateTimeOffset At, Guid EntryId, string Text, string Reasoning) : SessionEvent(At);
