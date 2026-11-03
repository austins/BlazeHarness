using BlazeHarness.Core.Agent.Events;
using BlazeHarness.Core.Agent.Questions;
using BlazeHarness.Core.Attachments;
using BlazeHarness.Core.Compaction;
using BlazeHarness.Core.Mcp.Tools;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Tools;
using Microsoft.Extensions.AI;
using System.Globalization;
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
    /// The text of the <see cref="StepMessage"/> added when a reply wrote a tool call as plain text. It has no chat entry of its own.
    /// </summary>
    internal const string TextCallNotice =
        "Your last reply wrote a tool call as plain text, so it was not run. Call the tool through the tool interface instead.";

    /// <summary>
    /// The number of rounds in a row in which every tool call failed before the runner tells the model to stop retrying.
    /// </summary>
    internal const int MaxFailedRounds = 5;

    /// <summary>
    /// The number of tool calls without an <c>update_todos</c> call, while the list has open items, before the runner reminds the model to update it.
    /// </summary>
    internal const int TodoReminderCalls = 10;

    /// <summary>
    /// The text that sets a runner notice apart from the tool output it is appended to.
    /// </summary>
    internal const string NoticePrefix = "[Harness notice] ";

    /// <summary>
    /// The notice added to the last tool result after <see cref="MaxFailedRounds"/> rounds in a row in which every tool call failed. It has no chat entry of its own.
    /// </summary>
    internal static readonly string FailureNotice = string.Create(
        CultureInfo.InvariantCulture,
        $"The last {MaxFailedRounds} rounds of tool calls all failed. Stop retrying. If {UserQuestion.ToolName} is available, use it to tell the user what failed and ask how to proceed; otherwise reply with what failed and what you need.");

    /// <summary>
    /// The notice added to the last tool result after <see cref="TodoReminderCalls"/> tool calls in which the model left an unfinished to-do list unchanged. It has no chat entry of its own.
    /// </summary>
    internal static readonly string TodoReminder = string.Create(
        CultureInfo.InvariantCulture,
        $"Your todo list has not changed in the last {TodoReminderCalls} tool calls. If a step is done or the plan changed, call {TodoList.ToolName} now; otherwise carry on.");

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
    /// <param name="inbox">Where messages the user wrote during the run are taken from, or null for none.</param>
    /// <param name="asker">Asks the user the questions the model puts with <c>ask_user</c>. Null means such a call fails, because nobody can be asked.</param>
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
        IRunInbox? inbox = null,
        IUserAsker? asker = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(time);
        return RunCoreAsync(client, history, options, tools, approver, permissions ?? new PermissionEngine(), context, inbox, asker, time, cancellationToken);
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
        IRunInbox? inbox,
        IUserAsker? asker,
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
        var failedRounds = 0;
        var callsSinceTodos = 0;
        var tracksTodos = tools?.Find(TodoList.ToolName) is not null;
        var questions = new QuestionBudget();
        while (true)
        {
            if (context is not null && context.ShouldCompact(history))
            {
                AgentEvent? compacted = context.TrimOutput(history);
                string? failure = null;
                var stopped = false;
                try
                {
                    compacted ??= await context.CompactAsync(history, manual: false, cancellationToken);
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
                    // The previous response is closed already, so the stop ends a new one instead of reopening it.
                    if (!firstResponse)
                    {
                        yield return new NewResponse();
                    }

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
                    history.Add(StepMessage.Create([new TextContent(TextCallNotice)]));
                    continue;
                }

                // End-of-run hook: a message the user queued during the run continues the loop instead of ending it.
                if (AddQueuedMessages(inbox, history, true, contents.Count == 0) is { } queued)
                {
                    yield return queued;
                    continue;
                }

                yield return new Done(StopReason.Completed);
                yield break;
            }

            textCallRetries = 0;

            var outcome = new RoundOutcome();
            await foreach (var e in ExecuteCallsAsync(calls, tools, approver, asker, questions, permissions, history, outcome, cancellationToken))
            {
                yield return e;
            }

            if (outcome.Cancelled)
            {
                yield return new Done(StopReason.Cancelled);
                yield break;
            }

            failedRounds = outcome.AllFailed ? failedRounds + 1 : 0;
            callsSinceTodos = calls.Exists(c => c.Name == TodoList.ToolName) ? 0 : callsSinceTodos + calls.Count;

            // The tool results are in the history now, so a user message can follow them.
            // A message from the user steers the run already, so it takes the place of the notice.
            // A notice goes into the last tool result rather than a user message: a new user message makes chat templates such as Qwen's
            // drop the thinking of the earlier steps, which changes the prompt from there on and throws away the server's prompt cache.
            if (AddQueuedMessages(inbox, history, false, false) is { } steering)
            {
                failedRounds = 0;
                yield return steering;
            }
            else if (failedRounds >= MaxFailedRounds)
            {
                failedRounds = 0;
                AddNotice(history, FailureNotice);
            }
            else if (tracksTodos && callsSinceTodos >= TodoReminderCalls)
            {
                callsSinceTodos = 0;
                if (TodoList.From(history).Items.Any(item => item.Status != TodoStatus.Completed))
                {
                    AddNotice(history, TodoReminder);
                }
            }
        }
    }

    /// <summary>
    /// Append a runner notice to the last tool result in the history.
    /// </summary>
    /// <param name="history">The history of the run, which holds the results of the round that just ended.</param>
    /// <param name="notice">The notice.</param>
    private static void AddNotice(IList<ChatMessage> history, string notice)
    {
        for (var m = history.Count - 1; m >= 0; m--)
        {
            var contents = history[m].Contents;
            for (var c = contents.Count - 1; history[m].Role == ChatRole.Tool && c >= 0; c--)
            {
                if (contents[c] is FunctionResultContent result)
                {
                    contents[c] = new FunctionResultContent(result.CallId, $"{result.Result}\n\n{NoticePrefix}{notice}");
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Add the messages the user queued to the history between two model calls.
    /// </summary>
    /// <remarks>
    /// Call it only where the history ends with a tool result or a reply without calls, never right after
    /// <see cref="TextCallNotice"/>, so two user messages never follow each other.
    /// </remarks>
    /// <param name="inbox">Where queued messages wait, or null when the run has none.</param>
    /// <param name="history">The history of the run.</param>
    /// <param name="replyEnded">True when the model has finished its reply, which takes every waiting message; false between two steps, which takes only steer messages.</param>
    /// <param name="afterEmptyReply">True when the last reply had no text and no calls, which the history does not hold; a placeholder reply is added so the roles alternate.</param>
    /// <returns>The event that tells the consumer to add a chat entry for the message, or null when nothing waited.</returns>
    private static MessagesInjected? AddQueuedMessages(IRunInbox? inbox, IList<ChatMessage> history, bool replyEnded, bool afterEmptyReply)
    {
        if (inbox?.TakeMessages(replyEnded) is not { Length: > 0 } text)
        {
            return null;
        }

        if (afterEmptyReply)
        {
            history.Add(new ChatMessage(ChatRole.Assistant, Chat.EmptyReplyText));
        }

        history.Add(new ChatMessage(ChatRole.User, text));
        return new MessagesInjected(text);
    }

    private static async IAsyncEnumerable<AgentEvent> ExecuteCallsAsync(
        List<FunctionCallContent> calls,
        ToolSet? tools,
        IToolApprover? approver,
        IUserAsker? asker,
        QuestionBudget questions,
        PermissionEngine permissions,
        IList<ChatMessage> history,
        RoundOutcome outcome,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        List<PlannedCall> planned = [];
        var results = new ToolResult?[calls.Count];
        var statuses = new ToolCallStatus[calls.Count];
        var askedInResponse = false;

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

                // A question is asked on its own and never runs as a tool, so it has no timeout and is not batched with reads.
                if (p.Tool is AskUserTool)
                {
                    var firstInResponse = !askedInResponse;
                    askedInResponse = true;
                    string? problem = null;
                    var question = firstInResponse ? ParseQuestion(p, questions, asker, out problem) : null;
                    if (question is null)
                    {
                        results[index] = ToolResult.Error(firstInResponse ? problem! : AskUserTool.OneAtATime);
                        statuses[index] = ToolCallStatus.Failed;
                        yield return Finished(p, results[index]!, statuses[index]);
                        index++;
                        continue;
                    }

                    questions.Asked++;
                    var reply = asker!.AskAsync(p.Call.CallId, question, cancellationToken);
                    yield return new ToolAwaitingAnswer(p.Call.CallId, question);
                    try
                    {
                        results[index] = ToolResult.Ok((await reply).ForModel());
                        statuses[index] = ToolCallStatus.Succeeded;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        outcome.Cancelled = true;
                        break;
                    }

                    yield return Finished(p, results[index]!, statuses[index]);
                    index++;
                    continue;
                }

                if (p.Tool!.IsReadOnly && decision.Verdict == PermissionVerdict.Allow)
                {
                    var contexts = new List<ToolContext> { ContextFor(tools, decision, permissions) };
                    while (index + contexts.Count < planned.Count
                        && planned[index + contexts.Count] is { CanRun: true, Tool.IsReadOnly: true, Tool: not AskUserTool } next
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
                    results[i] = ToolResult.Error(ToolCallEntry.Unfinished(ToolCallStatus.Cancelled, planned[i].Call.Name));
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
                    : ToolCallEntry.Unfinished(ToolCallStatus.Cancelled, calls[i].Name);
                answers.Add(new FunctionResultContent(calls[i].CallId, text));
            }

            history.Add(new ChatMessage(ChatRole.Tool, answers));
            if (AttachmentMessage.ForToolImages([.. calls.Select((c, i) => (c.Name, c.CallId, results[i]?.Image))]) is { } pictures)
            {
                history.Add(pictures);
            }

            outcome.AllFailed = statuses.All(status => status == ToolCallStatus.Failed);
        }
    }

    private static UserQuestion? ParseQuestion(PlannedCall call, QuestionBudget budget, IUserAsker? asker, out string? problem)
    {
        problem = null;
        UserQuestion question;
        try
        {
            question = AskUserTool.Parse(ToolArguments.Parse(call.Json));
        }
        catch (ToolArgumentException ex)
        {
            problem = ex.Message;
            return null;
        }

        if (budget.Asked >= AskUserTool.MaxPerRun)
        {
            problem = AskUserTool.TooMany;
            return null;
        }

        if (asker is null)
        {
            problem = AskUserTool.NoAsker;
            return null;
        }

        return question;
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
        return new ToolCallFinished(call.Call.CallId, call.Call.Name, result.Output, status, result.Image);
    }

    private static ToolContext ContextFor(ToolSet tools, PermissionDecision decision, PermissionEngine permissions)
    {
        var context = (decision.AllowOutside ? tools.Context.WithOutsideAllowed() : tools.Context).WithSensitive(permissions.Sensitive()).WithRefused(permissions.Refused(tools.Context.Root));
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

    private sealed class QuestionBudget
    {
        public int Asked { get; set; }
    }

    private sealed class RoundOutcome
    {
        public bool Cancelled { get; set; }

        public bool AllFailed { get; set; }
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
