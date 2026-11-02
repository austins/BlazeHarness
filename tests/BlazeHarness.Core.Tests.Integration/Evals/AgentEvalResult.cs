using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Agent.Events;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tests.Integration.Evals;

/// <summary>What an agent run did: its events and the conversation it left behind.</summary>
/// <param name="Events">Every event the run emitted, in order.</param>
/// <param name="History">The conversation after the run.</param>
internal sealed record AgentEvalResult(IReadOnlyList<AgentEvent> Events, IReadOnlyList<ChatMessage> History)
{
    /// <summary>Gets the names of the tools the model called, in order.</summary>
    public IReadOnlyList<string> ToolNames => [.. Events.OfType<ToolCallRequested>().Select(e => e.Name)];

    /// <summary>Gets how each tool call ended, in order.</summary>
    public IReadOnlyList<ToolCallStatus> Statuses => [.. Events.OfType<ToolCallFinished>().Select(e => e.Status)];

    /// <summary>Gets why the run ended.</summary>
    public StopReason Stop => Events.OfType<Done>().Single().Reason;

    /// <summary>Gets every tool result the model was shown, joined, to check what data reached it.</summary>
    public string ToolOutput => string.Join('\n', Events.OfType<ToolCallFinished>().Select(e => e.Output));
}
