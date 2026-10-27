namespace BlazeHarness.Core.Sessions;

/// <summary>
/// The model began a reply.
/// </summary>
/// <param name="At">When the reply began.</param>
/// <param name="EntryId">The id of the reply.</param>
/// <param name="Label">A label for the provider, model and level that produced it.</param>
internal sealed record AssistantStartEvent(DateTimeOffset At, Guid EntryId, string? Label) : SessionEvent(At);
