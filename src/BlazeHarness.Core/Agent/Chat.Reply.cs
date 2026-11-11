using BlazeHarness.Core.Agent.Events;
using BlazeHarness.Core.Attachments;
using BlazeHarness.Core.Chats.Events;
using BlazeHarness.Core.Compaction;
using BlazeHarness.Core.Providers;
using BlazeHarness.Core.Skills;
using BlazeHarness.Core.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text;

namespace BlazeHarness.Core.Agent;

public sealed partial class Chat
{
    private async Task<bool> ReplyAsync(ChatTarget target, IReadOnlyList<ChatEvent> leading, CancellationToken cancellation)
    {
        var startedAt = _time.GetUtcNow();
        var entry = new ChatEntry(ChatRole.Assistant)
        {
            Status = EntryStatus.Streaming,
            Label = target.Label,
            At = startedAt,
        };
        lock (_gate)
        {
            entry.HistoryIndex = _history.Count;
            _entries.Add(entry);
            Publish();
        }

        await SaveAsync([.. leading, new AssistantStartEvent(startedAt, entry.Id, entry.Label)]);
        RaiseChanged();

        List<ChatMessage> working = [.. History];
        var before = working.Count;
        var setup = Volatile.Read(ref _setup);
        var prompt = SystemPromptText(setup);
        if (prompt.Length > 0)
        {
            working.Insert(0, new ChatMessage(ChatRole.System, prompt));
            before++;
        }

        var reply = new Reply(entry, target, working, before, _time.GetTimestamp());
        try
        {
            using var client = new ImageOmittingChatClient(await _clientFactory(target, cancellation), AttachmentPolicy.AcceptsImages(target.Model));
            var options = ChatClientFactory.BuildOptions(target.Provider, target.Model, target.Level);
            var tools = await TurnToolsAsync(setup.Tools, cancellation);
            var toolTokens = tools is null ? 0 : TokenEstimate.OfTools(tools.Tools);
            Interlocked.Exchange(ref _toolTokens, toolTokens);
            EnsureToolsFit(target, prompt, toolTokens);
            var context = CreateContext(client, target, prompt.Length > 0 ? 1 : 0, toolTokens);
            var opening = MentionedSkillCalls(tools);
            await foreach (var e in ChatRunner.RunAsync(client, working, options, _time, tools, this, Permissions, context: context, inbox: this, asker: this, images: this, openingCalls: opening, cancellationToken: cancellation))
            {
                await ApplyAsync(reply, e);
                RaiseChanged();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            reply.Entry.Status = EntryStatus.Cancelled;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            reply.Entry.Status = EntryStatus.Failed;
            reply.Entry.Error = ex.Message;
            LogReplyFailed(ex);
        }
        finally
        {
            await CloseAsync(reply);
        }

        return reply.Entry.Status == EntryStatus.Done;
    }

    private static void EnsureToolsFit(ChatTarget target, string prompt, long toolTokens)
    {
        var limit = ContextBudget.Limit(target.Model);
        if (toolTokens == 0 || limit <= 0)
        {
            return;
        }

        var fixedTokens = TokenEstimate.Of(prompt) + toolTokens;
        if (fixedTokens > limit)
        {
            throw new InvalidOperationException(
                $"The instructions and tool definitions alone take about {fixedTokens:N0} tokens, more than the {limit:N0} this model can use before compacting. " +
                "Turn off some MCP servers for this chat, or untick \"Always load all of its tools\" in the MCP settings so the model finds tools by search instead.");
        }
    }

    private async Task<ToolSet?> TurnToolsAsync(ToolSet? tools, CancellationToken cancellation)
    {
        if (tools is null)
        {
            return null;
        }

        if (Skills is { Skills.Count: > 0 } skills)
        {
            tools = tools.With([new LoadSkillTool(skills, skills.StartedIn(Entries.Where(e => e.Role == ChatRole.User).Select(e => e.Text)))]);
        }

        return ExtraTools is { } extra ? tools.With(await extra(cancellation)) : tools;
    }

    /// <summary>
    /// Build a <c>load_skill</c> call for each skill the user's last message names as <c>/name</c>, so the reply starts with their instructions.
    /// </summary>
    /// <param name="tools">The tools of the reply.</param>
    /// <returns>The calls, or none when the message names no skill or the reply can't load skills.</returns>
    private List<FunctionCallContent> MentionedSkillCalls(ToolSet? tools)
    {
        if (tools?.Find(LoadSkillTool.ToolName) is null || Skills is not { } skills || Entries.LastOrDefault(e => e.Role == ChatRole.User) is not { } message)
        {
            return [];
        }

        // Some providers accept only short alphanumeric call IDs.
        return [
            .. skills.MentionedIn(message.Text).Select(name => new FunctionCallContent(
                Guid.NewGuid().ToString("N")[..9],
                LoadSkillTool.ToolName,
                new Dictionary<string, object?> { ["name"] = name }))
        ];
    }

    private async Task ApplyAsync(Reply reply, AgentEvent e)
    {
        var entry = reply.Entry;
        switch (e)
        {
            case TextDelta text:
                entry.AppendText(text.Text);
                reply.Text.Append(text.Text);
                await FlushIfDueAsync(reply);
                break;
            case ReasoningDelta thinking:
                entry.AppendReasoning(thinking.Text);
                reply.Reasoning.Append(thinking.Text);
                await FlushIfDueAsync(reply);
                break;
            case UsageReported usage:
                entry.Usage = usage;
                break;
            case AgentError error:
                entry.Error = error.Message;
                LogReplyEnded();
                break;
            case Done done:
                entry.Status = done.Reason switch
                {
                    StopReason.Cancelled => EntryStatus.Cancelled,
                    StopReason.Error => EntryStatus.Failed,
                    _ => EntryStatus.Done,
                };
                break;
            case NewResponse:
                await StartNextResponseAsync(reply);
                break;
            case MessagesInjected injected:
                await AddInjectedMessageAsync(reply, injected);
                break;
            case ContextCompacted compacted:
                await ApplyCompactionAsync(compacted);
                break;
            case ContextTrimmed trimmed:
                await ApplyTrimAsync(trimmed);
                break;
            case ContextCompactionFailed:
                entry.Warnings |= ReplyWarnings.CompactionFailed;
                LogCompactionFailed();
                break;
            case ContextTooLarge:
                entry.Warnings |= ReplyWarnings.ContextTooLarge;
                break;
            case ResponseTruncated:
                entry.Warnings |= ReplyWarnings.ResponseTruncated;
                break;
            case ToolCallRequested requested:
                if (entry.Status == EntryStatus.Streaming)
                {
                    await EndResponseAsync(reply);
                }

                entry.AddToolCall(new ToolCallEntry(requested.CallId, requested.Name, requested.ArgumentsJson, requested.Summary));
                await SaveAsync([new ToolCallEvent(_time.GetUtcNow(), entry.Id, requested.CallId, requested.Name, requested.ArgumentsJson, requested.Summary)]);
                break;
            case ToolAwaitingApproval awaiting:
                if (entry.ToolCalls.FirstOrDefault(c => c.CallId == awaiting.CallId) is { } waiting)
                {
                    waiting.ApprovalReason = awaiting.Reason;
                    waiting.CanRemember = awaiting.CanRemember;
                }

                SetStatus(entry, awaiting.CallId, ToolCallStatus.AwaitingApproval);
                break;
            case ToolChangePreview preview:
                if (entry.ToolCalls.FirstOrDefault(c => c.CallId == preview.CallId) is { } previewed)
                {
                    previewed.ChangePreview = preview.Lines;
                }

                break;
            case ToolAwaitingAnswer answering:
                SetStatus(entry, answering.CallId, ToolCallStatus.AwaitingAnswer);
                break;
            case ToolCallRunning running:
                if (entry.ToolCalls.FirstOrDefault(c => c.CallId == running.CallId) is { } started)
                {
                    started.StartedAt = _time.GetUtcNow();
                }

                SetStatus(entry, running.CallId, ToolCallStatus.Running);
                break;
            case ToolCallFinished finished:
                await FinishCallAsync(entry, finished);
                break;
            default:
                break;
        }
    }

    private static void SetStatus(ChatEntry entry, string callId, ToolCallStatus status)
    {
        if (entry.ToolCalls.FirstOrDefault(c => c.CallId == callId) is { } call)
        {
            call.Status = status;
        }
    }

    private async Task FinishCallAsync(ChatEntry entry, ToolCallFinished finished)
    {
        if (entry.ToolCalls.FirstOrDefault(c => c.CallId == finished.CallId) is not { } call)
        {
            return;
        }

        call.Images = [.. (finished.Images ?? []).Select(i => i.Attachment).OfType<AttachmentInfo>()];
        call.Output = finished.Output;
        call.Status = finished.Status;
        call.EndLiveOutput();
        await SaveAsync([new ToolResultEvent(_time.GetUtcNow(), entry.Id, finished.CallId, finished.Status, finished.Output, Images: call.Images.Count > 0 ? call.Images : null)]);
    }

    private async Task FlushIfDueAsync(Reply reply)
    {
        if (_time.GetElapsedTime(reply.LastSave) >= FlushInterval)
        {
            await SaveAsync([TakeDelta(reply)]);
            reply.LastSave = _time.GetTimestamp();
        }
    }

    /// <summary>
    /// Finish and save the current assistant response.
    /// </summary>
    /// <remarks>The text is complete once its tool calls are known, so it is saved before the calls run.</remarks>
    /// <param name="reply">The reply whose current response is complete.</param>
    private Task EndResponseAsync(Reply reply)
    {
        var entry = reply.Entry;
        entry.Status = EntryStatus.Done;
        entry.At = _time.GetUtcNow();
        List<ChatEvent> events = [];
        if (reply.Text.Length > 0 || reply.Reasoning.Length > 0)
        {
            events.Add(TakeDelta(reply));
        }

        events.Add(new AssistantEndEvent(entry.At, entry.Id, entry.Status, entry.Error, entry.Usage, entry.Warnings));
        return SaveAsync(events);
    }

    /// <summary>
    /// Finish any open response and start the next assistant response.
    /// </summary>
    /// <remarks>A response without tool calls, such as one the runner asks again after, is still open here.</remarks>
    /// <param name="reply">The reply receiving the next response.</param>
    private async Task StartNextResponseAsync(Reply reply)
    {
        if (reply.Entry.Status == EntryStatus.Streaming)
        {
            await EndResponseAsync(reply);
        }

        var startedAt = _time.GetUtcNow();
        var next = new ChatEntry(ChatRole.Assistant)
        {
            Status = EntryStatus.Streaming,
            Label = reply.Target.Label,
            At = startedAt,
        };
        lock (_gate)
        {
            next.HistoryIndex = _history.Count + reply.Working.Count - reply.Before;
            _entries.Add(next);
            Publish();
        }

        reply.Entry = next;
        await SaveAsync([new AssistantStartEvent(startedAt, next.Id, next.Label)]);
    }

    /// <summary>
    /// Add and save the user entry for an injected message.
    /// </summary>
    /// <remarks>The loop has already appended the message to the working history, so the entry points at that index.</remarks>
    /// <param name="reply">The reply receiving the message.</param>
    /// <param name="injected">The injected message.</param>
    private async Task AddInjectedMessageAsync(Reply reply, MessagesInjected injected)
    {
        if (reply.Entry.Status == EntryStatus.Streaming)
        {
            await EndResponseAsync(reply);
        }

        var sentAt = _time.GetUtcNow();
        ChatSource? source;
        lock (_gate)
        {
            source = _takenSource;
            _takenSource = null;
        }

        var origin = source is null ? UserMessageOrigin.Steer : UserMessageOrigin.FromChat;
        var user = new ChatEntry(ChatRole.User, injected.Text) { Status = EntryStatus.Done, At = sentAt, Origin = origin, Source = source };
        lock (_gate)
        {
            user.HistoryIndex = _history.Count + reply.Working.Count - reply.Before - 1;
            _entries.Add(user);
            Publish();
        }

        await SaveAsync([new UserMessageEvent(sentAt, user.Id, injected.Text, null, origin, source)]);
    }

    private Task CloseAsync(Reply reply)
    {
        var entry = reply.Entry;
        if (entry.Status == EntryStatus.Streaming)
        {
            entry.Status = EntryStatus.Done;
        }

        lock (_gate)
        {
            _history.AddRange(reply.Working.Skip(reply.Before));
            if (entry.Text.Length == 0 && entry.ToolCalls.Count == 0)
            {
                _history.Add(new ChatMessage(ChatRole.Assistant, EmptyReplyText));
            }
        }

        var endedAt = _time.GetUtcNow();
        List<ChatEvent> closing = [];
        if (reply.Text.Length > 0 || reply.Reasoning.Length > 0)
        {
            closing.Add(TakeDelta(reply));
        }

        foreach (var call in entry.ToolCalls.Where(c => !c.IsFinished))
        {
            call.Status = ToolCallStatus.Cancelled;
            call.Output = ToolCallEntry.Unfinished(ToolCallStatus.Cancelled, call.Name);
            closing.Add(new ToolResultEvent(endedAt, entry.Id, call.CallId, call.Status, call.Output));
        }

        entry.At = endedAt;
        closing.Add(new AssistantEndEvent(endedAt, entry.Id, entry.Status, entry.Error, entry.Usage, entry.Warnings));
        return SaveAsync(closing);
    }

    private AssistantDeltaEvent TakeDelta(Reply reply)
    {
        var delta = new AssistantDeltaEvent(_time.GetUtcNow(), reply.Entry.Id, reply.Text.ToString(), reply.Reasoning.ToString());
        reply.Text.Clear();
        reply.Reasoning.Clear();
        return delta;
    }

    private async Task SaveAsync(IReadOnlyCollection<ChatEvent> events)
    {
        if (_log is null)
        {
            return;
        }

        try
        {
            await _log.AppendAsync(events);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            LogSaveFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not keep a picture a tool call produced; a resumed chat won't show it to the model.")]
    private partial void LogImageNotKept(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A reply failed.")]
    private partial void LogReplyFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A reply ended with an error.")]
    private partial void LogReplyEnded();

    private sealed class Reply(ChatEntry entry, ChatTarget target, List<ChatMessage> working, int before, long lastSave)
    {
        public ChatEntry Entry { get; set; } = entry;

        public ChatTarget Target { get; } = target;

        public List<ChatMessage> Working { get; } = working;

        public int Before { get; } = before;

        public StringBuilder Text { get; } = new();

        public StringBuilder Reasoning { get; } = new();

        public long LastSave { get; set; } = lastSave;
    }
}
