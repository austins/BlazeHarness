namespace BlazeHarness.Core.Agent;

/// <summary>
/// The model requested a tool call. Its response is complete, and the call has not started.
/// </summary>
/// <param name="CallId">The provider's identifier for the call, used to pair it with its result.</param>
/// <param name="Name">The name of the tool being called.</param>
/// <param name="ArgumentsJson">The call arguments as a JSON object string.</param>
/// <param name="Summary">A few words describing the call, or an empty string.</param>
internal sealed record ToolCallRequested(string CallId, string Name, string ArgumentsJson, string Summary) : AgentEvent;
