using BlazeHarness.Core.Compaction;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Tools;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace BlazeHarness.Core.Agent;

/// <summary>
/// Runs an assistant turn against a chat client, calling tools the model asks for, and reports it as a stream of <see cref="AgentEvent"/> values.
/// </summary>
public static class ChatRunner
{
    /// <summary>
    /// The default most times the model is called in one run, so a model stuck in a loop can't run forever.
    /// </summary>
    public const int DefaultMaxRounds = 50;

    private static readonly JsonSerializerOptions CompactJson = new(AIJsonUtilities.DefaultOptions) { WriteIndented = false };

    /// <summary>
    /// Stream the assistant's reply to the conversation so far, running tool calls until the model answers without one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The messages of the run are appended to <paramref name="history"/>, including a partial reply after a cancellation or an error,
    /// so the next turn sees what the user saw. Every tool call gets a result in the history, even one that never ran,
    /// because providers reject a call without one. The stream always ends with a <see cref="Done"/>.
    /// </para>
    /// <para>
    /// Each call is checked by <paramref name="permissions"/> first. Consecutive read-only calls of one response that need no approval run at the same time.
    /// A call that needs approval asks <paramref name="approver"/> first and runs alone; without an approver it is denied.
    /// </para>
    /// </remarks>
    /// <param name="client">The chat client to call.</param>
    /// <param name="history">The conversation so far, ending with the user's message.</param>
    /// <param name="options">The request options, including sampling and thinking fields. The tool definitions are added to it.</param>
    /// <param name="tools">The tools the model may call, or null for a reply without tools.</param>
    /// <param name="approver">Asks the user before a call that needs approval runs.</param>
    /// <param name="permissions">Decides which calls run, ask or are refused. Null means the default settings.</param>
    /// <param name="maxRounds">The most times to call the model.</param>
    /// <param name="context">Chooses what part of <paramref name="history"/> is sent and compacts it before a call when it has grown too large. Null sends all of it.</param>
    /// <param name="cancellationToken">A token that stops the run.</param>
    /// <returns>The events of the run.</returns>
    public static IAsyncEnumerable<AgentEvent> RunAsync(
        IChatClient client,
        IList<ChatMessage> history,
        ChatOptions options,
        ToolSet? tools = null,
        IToolApprover? approver = null,
        PermissionEngine? permissions = null,
        int maxRounds = DefaultMaxRounds,
        ConversationContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRounds, 1);

        return RunCoreAsync(client, history, options, tools, approver, permissions ?? new PermissionEngine(), maxRounds, context, cancellationToken);
    }

    private static async IAsyncEnumerable<AgentEvent> RunCoreAsync(
        IChatClient client,
        IList<ChatMessage> history,
        ChatOptions options,
        ToolSet? tools,
        IToolApprover? approver,
        PermissionEngine permissions,
        int maxRounds,
        ConversationContext? context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (tools is { Declarations.Count: > 0 })
        {
            options.Tools = [.. tools.Declarations];
        }

        for (var round = 0; round < maxRounds; round++)
        {
            if (context is not null && context.ShouldCompact(history))
            {
                ContextCompacted? compacted = null;
                string? failure = null;
                var stopped = false;
                try
                {
                    compacted = await context.CompactAsync(history, manual: false, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    stopped = true;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    failure = ex.Message;
                    context.Suspend();
                }

                if (stopped)
                {
                    yield return new Done(StopReason.Cancelled);
                    yield break;
                }

                if (compacted is not null)
                {
                    yield return compacted;
                }
                else if (failure is not null)
                {
                    yield return new ContextCompactionFailed(failure);
                }
            }

            if (round > 0)
            {
                yield return new NewResponse();
            }

            var requestMessages = context?.View(history) ?? history;
            var requestCount = history.Count;

            var reply = new StringBuilder();
            var calls = new List<FunctionCallContent>();
            var reason = StopReason.Completed;
            string? error = null;

            var stream = client.GetStreamingResponseAsync(requestMessages, options, cancellationToken);
            await using var enumerator = stream.GetAsyncEnumerator(cancellationToken);

            while (true)
            {
                ChatResponseUpdate update;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        break;
                    }

                    update = enumerator.Current;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    reason = StopReason.Cancelled;
                    break;
                }
                catch (Exception ex)
                {
                    reason = StopReason.Error;
                    error = ex.Message;
                    break;
                }

                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case TextReasoningContent { Text.Length: > 0 } thinking:
                            yield return new ReasoningDelta(thinking.Text);
                            break;
                        case TextContent { Text.Length: > 0 } text:
                            reply.Append(text.Text);
                            yield return new TextDelta(text.Text);
                            break;
                        case FunctionCallContent call:
                            calls.Add(call);
                            break;
                        case UsageContent usage:
                            context?.RecordUsage(usage.Details.InputTokenCount ?? 0, requestCount);
                            yield return new UsageReported(
                                usage.Details.InputTokenCount ?? 0,
                                usage.Details.OutputTokenCount ?? 0,
                                usage.Details.CachedInputTokenCount ?? 0);
                            break;
                        default:
                            break;
                    }
                }
            }

            if (reason != StopReason.Completed)
            {
                // Calls that arrived before a failure are dropped, since nothing would answer them.
                if (reply.Length > 0)
                {
                    history.Add(new ChatMessage(ChatRole.Assistant, reply.ToString()));
                }

                if (error is not null)
                {
                    yield return new AgentError(error);
                }

                yield return new Done(reason);
                yield break;
            }

            List<AIContent> contents = [];
            if (reply.Length > 0)
            {
                contents.Add(new TextContent(reply.ToString()));
            }

            contents.AddRange(calls);
            if (contents.Count > 0)
            {
                history.Add(new ChatMessage(ChatRole.Assistant, contents));
            }

            if (calls.Count == 0)
            {
                yield return new Done(StopReason.Completed);
                yield break;
            }

            var outcome = new RoundOutcome();
            await foreach (var e in ExecuteCallsAsync(calls, tools, approver, permissions, history, outcome, cancellationToken))
            {
                yield return e;
            }

            if (outcome.Cancelled)
            {
                yield return new Done(StopReason.Cancelled);
                yield break;
            }
        }

        yield return new AgentError($"The run stopped after {maxRounds} model calls without finishing.");
        yield return new Done(StopReason.MaxIterations);
    }

    private static async IAsyncEnumerable<AgentEvent> ExecuteCallsAsync(
        List<FunctionCallContent> calls,
        ToolSet? tools,
        IToolApprover? approver,
        PermissionEngine permissions,
        IList<ChatMessage> history,
        RoundOutcome outcome,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var planned = calls.ConvertAll(c => new PlannedCall(c, tools?.Find(c.Name), ArgumentsJson(c)));
        var results = new ToolResult?[planned.Count];
        var statuses = new ToolCallStatus[planned.Count];

        try
        {
            foreach (var p in planned)
            {
                yield return new ToolCallRequested(p.Call.CallId, p.Call.Name, p.Json, p.Tool is null ? string.Empty : ToolSet.Summarize(p.Tool, p.Json));
            }

            var index = 0;
            while (index < planned.Count && !outcome.Cancelled)
            {
                var p = planned[index];
                if (!p.CanRun)
                {
                    results[index] = ToolResult.Error(p.Problem(tools));
                    statuses[index] = ToolCallStatus.Failed;
                    yield return Finished(p, results[index]!, statuses[index]);
                    index++;
                    continue;
                }

                var decision = permissions.Evaluate(p.Tool!, p.Json, tools!.Context);
                if (decision.Verdict == PermissionVerdict.Deny)
                {
                    results[index] = ToolResult.Error(decision.Reason);
                    statuses[index] = ToolCallStatus.Denied;
                    yield return Finished(p, results[index]!, statuses[index]);
                    index++;
                    continue;
                }

                if (p.Tool!.IsReadOnly && decision.Verdict == PermissionVerdict.Allow)
                {
                    var contexts = new List<ToolContext> { ContextFor(tools, decision) };
                    while (index + contexts.Count < planned.Count
                        && planned[index + contexts.Count] is { CanRun: true, Tool.IsReadOnly: true } next
                        && permissions.Evaluate(next.Tool, next.Json, tools.Context) is { Verdict: PermissionVerdict.Allow } nextDecision)
                    {
                        contexts.Add(ContextFor(tools, nextDecision));
                    }

                    var end = index + contexts.Count;
                    for (var i = index; i < end; i++)
                    {
                        yield return new ToolCallRunning(planned[i].Call.CallId);
                    }

                    var batch = Enumerable.Range(index, contexts.Count).Select(i => RunAsync(tools, planned[i], contexts[i - index], cancellationToken)).ToList();
                    ToolResult[]? done = null;
                    try
                    {
                        done = await Task.WhenAll(batch);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        outcome.Cancelled = true;
                    }

                    for (var i = index; i < end && done is not null; i++)
                    {
                        results[i] = done[i - index];
                        statuses[i] = results[i]!.IsError ? ToolCallStatus.Failed : ToolCallStatus.Succeeded;
                        yield return Finished(planned[i], results[i]!, statuses[i]);
                    }

                    index = end;
                    continue;
                }

                var allowed = decision.Verdict == PermissionVerdict.Allow;
                if (!allowed && approver is not null)
                {
                    var request = new ApprovalRequest(p.Call.CallId, p.Call.Name, p.Json, decision.Reason, decision.GrantKey is not null);
                    var answer = approver.RequestAsync(request, cancellationToken);
                    yield return new ToolAwaitingApproval(p.Call.CallId, decision.Reason, request.CanRemember);
                    try
                    {
                        var choice = await answer;
                        allowed = choice != ApprovalChoice.Deny;
                        if (choice == ApprovalChoice.AllowForSession && decision.GrantKey is { } key)
                        {
                            permissions.Remember(key);
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        outcome.Cancelled = true;
                        break;
                    }
                }

                if (!allowed)
                {
                    results[index] = ToolResult.Error(approver is null
                        ? "This call was not run because there is no way to ask the user for approval."
                        : "The user denied this call. Do not retry it; say what you could not do and suggest an alternative.");
                    statuses[index] = ToolCallStatus.Denied;
                    yield return Finished(p, results[index]!, statuses[index]);
                    index++;
                    continue;
                }

                yield return new ToolCallRunning(p.Call.CallId);
                try
                {
                    results[index] = await RunAsync(tools, p, ContextFor(tools, decision), cancellationToken);
                    statuses[index] = results[index]!.IsError ? ToolCallStatus.Failed : ToolCallStatus.Succeeded;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    outcome.Cancelled = true;
                    break;
                }

                yield return Finished(p, results[index]!, statuses[index]);
                index++;
            }

            for (var i = 0; i < planned.Count; i++)
            {
                if (results[i] is null)
                {
                    statuses[i] = ToolCallStatus.Cancelled;
                    results[i] = ToolResult.Error(ToolCallEntry.Unfinished(ToolCallStatus.Cancelled));
                    yield return Finished(planned[i], results[i]!, statuses[i]);
                }
            }
        }
        finally
        {
            List<AIContent> answers = [];
            for (var i = 0; i < planned.Count; i++)
            {
                var text = results[i] is { } result
                    ? ToolCallEntry.ForModel(statuses[i], result.Output)
                    : ToolCallEntry.Unfinished(ToolCallStatus.Cancelled);
                answers.Add(new FunctionResultContent(planned[i].Call.CallId, text));
            }

            history.Add(new ChatMessage(ChatRole.Tool, answers));
        }
    }

    private static ToolCallFinished Finished(PlannedCall call, ToolResult result, ToolCallStatus status)
    {
        return new ToolCallFinished(call.Call.CallId, call.Call.Name, result.Output, status);
    }

    private static ToolContext ContextFor(ToolSet tools, PermissionDecision decision)
    {
        var context = decision.AllowOutside ? tools.Context.WithOutsideAllowed() : tools.Context;
        if (decision.ShellReadFolders is { Count: > 0 } folders)
        {
            context = context.WithShellReadFolders(folders);
        }

        return decision.Shell == context.Shell ? context : context.WithShell(decision.Shell);
    }

    private static async Task<ToolResult> RunAsync(ToolSet tools, PlannedCall call, ToolContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await tools.ExecuteAsync(call.Tool!, call.Json, cancellationToken, context);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            return ToolResult.Error($"The tool failed unexpectedly: {ex.Message}");
        }
    }

    private static string ArgumentsJson(FunctionCallContent call)
    {
        return call.Arguments is null ? "{}" : JsonSerializer.Serialize(call.Arguments, CompactJson);
    }

    private sealed class RoundOutcome
    {
        public bool Cancelled { get; set; }
    }

    private sealed record PlannedCall(FunctionCallContent Call, ITool? Tool, string Json)
    {
        public bool CanRun => Tool is not null && Call.Exception is null;

        public string Problem(ToolSet? tools)
        {
            if (Tool is null)
            {
                var names = tools is null ? "none" : string.Join(", ", tools.Tools.Select(t => t.Name));
                return $"There is no tool named '{Call.Name}'. The available tools are: {names}.";
            }

            return "The arguments are not valid JSON. Send a JSON object.";
        }
    }
}
