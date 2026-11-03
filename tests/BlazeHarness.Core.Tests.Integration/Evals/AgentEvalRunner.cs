using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Agent.Events;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;

namespace BlazeHarness.Core.Tests.Integration.Evals;

/// <summary>
/// Runs a scripted model against the real file tools and permission engine in a real folder, so a test can assert on what the harness did.
/// </summary>
internal static class AgentEvalRunner
{
    /// <summary>Run the agent loop to the end.</summary>
    /// <param name="folder">The working folder the tools run in.</param>
    /// <param name="client">The scripted model.</param>
    /// <param name="permissions">The permission engine, or null for the defaults.</param>
    /// <param name="approver">Answers approval questions, or null when nobody can be asked.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>What the run did.</returns>
    public static async Task<AgentEvalResult> RunAsync(
        string folder,
        IChatClient client,
        PermissionEngine? permissions,
        IToolApprover? approver,
        CancellationToken cancellationToken)
    {
        var time = new FakeTimeProvider();
        var tools = BuiltInTools.Create(folder, time);
        List<ChatMessage> history = [new(ChatRole.User, "Do the task.")];
        List<AgentEvent> events = [];
        await foreach (var e in ChatRunner.RunAsync(client, history, new ChatOptions(), time, tools, approver, permissions, context: null, cancellationToken: cancellationToken))
        {
            events.Add(e);
        }

        return new AgentEvalResult(events, history);
    }
}
