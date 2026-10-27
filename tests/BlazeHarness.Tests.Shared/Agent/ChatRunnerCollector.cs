using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Compaction;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Tools;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Tests.Shared.Agent;

/// <summary>Runs the chat runner to the end and gathers its events.</summary>
public static class ChatRunnerCollector
{
    /// <summary>Run the chat runner and collect every event it emits.</summary>
    /// <param name="client">The chat client to call.</param>
    /// <param name="history">The conversation; the runner appends to it.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <param name="tools">The tools the model may call.</param>
    /// <param name="approver">Asks the user to approve tool calls.</param>
    /// <param name="permissions">The permission rules for tool calls.</param>
    /// <param name="maxRounds">The most model rounds the run may take.</param>
    /// <param name="context">The compaction state, when the test needs it.</param>
    /// <returns>The events in the order they were emitted.</returns>
    public static async Task<List<AgentEvent>> CollectAsync(
        IChatClient client,
        IList<ChatMessage> history,
        CancellationToken cancellationToken,
        ToolSet? tools = null,
        IToolApprover? approver = null,
        PermissionEngine? permissions = null,
        int maxRounds = ChatRunner.DefaultMaxRounds,
        ConversationContext? context = null)
    {
        var events = new List<AgentEvent>();
        await foreach (var e in ChatRunner.RunAsync(client, history, new ChatOptions(), tools, approver, permissions, maxRounds, context, cancellationToken))
        {
            events.Add(e);
        }

        return events;
    }
}
