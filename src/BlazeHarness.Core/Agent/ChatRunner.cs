using BlazeHarness.Core.Agent.Events;
using BlazeHarness.Core.Compaction;
using BlazeHarness.Core.Mcp.Tools;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Tools;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using McpToolNames = BlazeHarness.Core.Mcp.Tools.McpToolNames;

namespace BlazeHarness.Core.Agent;

/// <summary>
/// Runs an assistant turn against a chat client, calling tools the model asks for, and reports it as a stream of <see cref="AgentEvent"/> values.
/// </summary>
internal static class ChatRunner
{
    /// <summary>
    /// The user message added when a reply wrote a tool call as plain text. It has no chat entry of its own.
    /// </summary>
    internal const string TextCallNotice =
        "Your last reply wrote a tool call as plain text, so it was not run. Call the tool through the tool interface instead.";

    private const int MaxTextCallRetries = 3;
    private const string TextCallMarker = "<tool_call>";

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
    /// <param name="time">The clock that times each response.</param>
    /// <param name="tools">The tools the model may call, or null for a reply without tools.</param>
    /// <param name="approver">Asks the user before a call that needs approval runs.</param>
    /// <param name="permissions">Decides which calls run, ask or are refused. Null means the default settings.</param>
    /// <param name="context">Chooses what part of <paramref name="history"/> is sent and compacts it before a call when it has grown too large. Null sends all of it.</param>
    /// <param name="cancellationToken">A token that stops the run.</param>
    /// <returns>The events of the run.</returns>
    public static IAsyncEnumerable<AgentEvent> RunAsync(
        IChatClient client,
        IList<ChatMessage> history,
        ChatOptions options,
        TimeProvider time,
        ToolSet? tools = null,
        IToolApprover? approver = null,
        PermissionEngine? permissions = null,
        ConversationContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(time);
        return RunCoreAsync(client, history, options, tools, approver, permissions ?? new PermissionEngine(), context, time, cancellationToken);
    }

    /// <summary>
    /// Get whether a reply wrote a tool call as plain text, which makes the runner add <see cref="TextCallNotice"/> and ask again.
    /// </summary>
    /// <param name="text">The reply text.</param>
    /// <returns>True when the reply starts with the tool call marker.</returns>
    internal static bool WroteCallAsText(string text) => text.AsSpan().TrimStart().StartsWith(TextCallMarker, StringComparison.Ordinal);

    private static TimeSpan? Positive(TimeSpan span)
    {
        return span > TimeSpan.Zero ? span : null;
    }

    private static async IAsyncEnumerable<AgentEvent> RunCoreAsync(
        IChatClient client,
        IList<ChatMessage> history,
        ChatOptions options,
        ToolSet? tools,
        IToolApprover? approver,
        PermissionEngine permissions,
        ConversationContext? context,
        TimeProvider time,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (tools is { Declarations.Count: > 0 })
        {
            options.Tools = [.. tools.Declarations];
        }

        // There is no limit on rounds: the user can stop a run, and compaction keeps the context in bounds.
        var firstResponse = true;
        var textCallRetries = 0;
        while (true)
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

            if (!firstResponse)
            {
                yield return new NewResponse();
            }

            firstResponse = false;

            var requestMessages = context?.View(history) ?? history;
            var requestCount = history.Count;

            var reply = new StringBuilder();
            var thinking = new StringBuilder();
            var calls = new List<FunctionCallContent>();
            var reason = StopReason.Completed;
            var truncated = false;
            string? error = null;

            UsageDetails? usageDetails = null;
            long? firstOutput = null;
            long lastOutput = 0;
            var requested = time.GetTimestamp();
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

                truncated |= update.FinishReason == ChatFinishReason.Length;
                foreach (var content in update.Contents)
                {
                    if (content is TextReasoningContent or TextContent or FunctionCallContent)
                    {
                        lastOutput = time.GetTimestamp();
                        firstOutput ??= lastOutput;
                    }

                    switch (content)
                    {
                        case TextReasoningContent { Text.Length: > 0 } reasoning:
                            thinking.Append(reasoning.Text);
                            yield return new ReasoningDelta(reasoning.Text);
                            break;
                        case TextContent { Text.Length: > 0 } text:
                            reply.Append(text.Text);
                            yield return new TextDelta(text.Text);
                            break;
                        case FunctionCallContent call:
                            calls.Add(call);
                            break;
                        case UsageContent usage:
                            usageDetails = usage.Details;
                            break;
                        default:
                            break;
                    }
                }
            }

            if (usageDetails is not null)
            {
                context?.RecordUsage(usageDetails.InputTokenCount ?? 0, requestCount);
                yield return new UsageReported(
                    usageDetails.InputTokenCount ?? 0,
                    usageDetails.OutputTokenCount ?? 0,
                    usageDetails.CachedInputTokenCount ?? 0,
                    usageDetails.ReasoningTokenCount ?? 0,
                    firstOutput is { } first ? Positive(time.GetElapsedTime(requested, first)) : null,
                    firstOutput is { } start ? Positive(time.GetElapsedTime(start, lastOutput)) : null);
            }

            if (reason == StopReason.Completed && truncated)
            {
                yield return new ResponseTruncated();
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
                // Kept so the next call of this run can see what the model worked out; see ReasoningReplayChatClient.
                if (thinking.Length > 0)
                {
                    contents.Insert(0, new TextReasoningContent(thinking.ToString()));
                }

                history.Add(new ChatMessage(ChatRole.Assistant, contents));
            }

            if (calls.Count == 0)
            {
                // Some models write the call as text when the server's parser misses it; nothing ran, so tell the model.
                if (tools is { Declarations.Count: > 0 }
                    && textCallRetries < MaxTextCallRetries
                    && WroteCallAsText(reply.ToString()))
                {
                    textCallRetries++;
                    history.Add(new ChatMessage(ChatRole.User, TextCallNotice));
                    continue;
                }

                yield return new Done(StopReason.Completed);
                yield break;
            }

            textCallRetries = 0;

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
        List<PlannedCall> planned = [];
        var results = new ToolResult?[calls.Count];
        var statuses = new ToolCallStatus[calls.Count];

        try
        {
            foreach (var call in calls)
            {
                try
                {
                    planned.Add(await PlanAsync(call, tools, cancellationToken));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    outcome.Cancelled = true;
                    yield break;
                }
            }

            foreach (var p in planned)
            {
                yield return new ToolCallRequested(p.Call.CallId, p.Call.Name, p.RawJson, p.Summary());
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
                    var contexts = new List<ToolContext> { ContextFor(tools, decision, permissions) };
                    while (index + contexts.Count < planned.Count
                        && planned[index + contexts.Count] is { CanRun: true, Tool.IsReadOnly: true } next
                        && permissions.Evaluate(next.Tool, next.Json, tools.Context) is { Verdict: PermissionVerdict.Allow } nextDecision)
                    {
                        contexts.Add(ContextFor(tools, nextDecision, permissions));
                    }

                    var end = index + contexts.Count;
                    for (var i = index; i < end; i++)
                    {
                        yield return new ToolCallRunning(planned[i].Call.CallId);
                    }

                    // A call that finished before a stop keeps its result; the ones that were stopped are reported as cancelled below.
                    var done = await Task.WhenAll(Enumerable.Range(index, contexts.Count).Select(i => RunUnlessStoppedAsync(tools, planned[i], contexts[i - index], cancellationToken)));
                    outcome.Cancelled = Array.Exists(done, result => result is null);
                    for (var i = index; i < end; i++)
                    {
                        if (done[i - index] is not { } result)
                        {
                            continue;
                        }

                        results[i] = result;
                        statuses[i] = result.IsError ? ToolCallStatus.Failed : ToolCallStatus.Succeeded;
                        yield return Finished(planned[i], result, statuses[i]);
                    }

                    index = end;
                    continue;
                }

                var allowed = decision.Verdict == PermissionVerdict.Allow;
                if (!allowed && approver is not null)
                {
                    var request = new ApprovalRequest(p.Call.CallId, p.Name, p.Json, decision.Reason, decision.GrantKey is not null);
                    var answer = approver.RequestAsync(request, cancellationToken);
                    yield return new ToolAwaitingApproval(p.Call.CallId, decision.Reason, request.CanRemember);
                    try
                    {
                        var choice = await answer;
                        allowed = choice != ApprovalChoice.Deny;
                        if (choice == ApprovalChoice.AllowForChat && decision.GrantKey is { } key)
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
                    results[index] = await RunAsync(tools, p, ContextFor(tools, decision, permissions), cancellationToken);
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
            for (var i = 0; i < calls.Count; i++)
            {
                var text = results[i] is { } result
                    ? ToolCallEntry.ForModel(statuses[i], result.Output)
                    : ToolCallEntry.Unfinished(ToolCallStatus.Cancelled);
                answers.Add(new FunctionResultContent(calls[i].CallId, text));
            }

            history.Add(new ChatMessage(ChatRole.Tool, answers));
        }
    }

    // A call to a forwarding tool is planned as the tool it names, so permissions and approvals judge the tool that really runs.
    private static async Task<PlannedCall> PlanAsync(FunctionCallContent call, ToolSet? tools, CancellationToken cancellationToken)
    {
        var raw = ArgumentsJson(call);
        var tool = tools?.Find(call.Name);
        if (tool is not IForwardingTool forwarding || call.Exception is not null)
        {
            return new PlannedCall(call, tool, raw, raw, call.Name, null);
        }

        try
        {
            var forwarded = await forwarding.ResolveAsync(ToolArguments.Parse(raw), cancellationToken);
            return new PlannedCall(call, forwarded.Tool, forwarded.ArgumentsJson, raw, forwarded.Name, forwarded.Problem);
        }
        catch (ToolArgumentException ex)
        {
            return new PlannedCall(call, null, raw, raw, call.Name, ex.Message);
        }
    }

    private static ToolCallFinished Finished(PlannedCall call, ToolResult result, ToolCallStatus status)
    {
        return new ToolCallFinished(call.Call.CallId, call.Call.Name, result.Output, status);
    }

    private static ToolContext ContextFor(ToolSet tools, PermissionDecision decision, PermissionEngine permissions)
    {
        var context = (decision.AllowOutside ? tools.Context.WithOutsideAllowed() : tools.Context).WithSensitive(permissions.Sensitive());
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

    private static async Task<ToolResult?> RunUnlessStoppedAsync(ToolSet tools, PlannedCall call, ToolContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await RunAsync(tools, call, context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
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

    // Json holds the arguments of the tool that runs, which for a forwarded call are not the ones the model sent (RawJson).
    private sealed record PlannedCall(FunctionCallContent Call, ITool? Tool, string Json, string RawJson, string Name, string? Error)
    {
        public bool CanRun => Tool is not null && Call.Exception is null;

        public string Summary()
        {
            if (Tool is null)
            {
                return string.Empty;
            }

            var summary = ToolSet.Summarize(Tool, Json);
            return Name == Call.Name ? summary : $"{Name} {summary}".TrimEnd();
        }

        public string Problem(ToolSet? tools)
        {
            if (Error is not null)
            {
                return Error;
            }

            if (Tool is null)
            {
                var names = tools is null ? "none" : string.Join(", ", tools.Tools.Select(t => t.Name));
                var hint = tools?.Find(UseToolTool.ToolName) is null || !Call.Name.Contains(McpToolNames.Separator, StringComparison.Ordinal)
                    ? string.Empty
                    : $" If '{Call.Name}' is a tool of an MCP server, call it through {UseToolTool.ToolName}.";
                return $"There is no tool named '{Call.Name}'. The available tools are: {names}.{hint}";
            }

            return "The arguments are not valid JSON. Send a JSON object.";
        }
    }
}
