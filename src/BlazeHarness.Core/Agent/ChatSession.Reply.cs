using BlazeHarness.Core.Providers;
using BlazeHarness.Core.Sessions;
using Microsoft.Extensions.AI;
using System.Text;

namespace BlazeHarness.Core.Agent;

public sealed partial class ChatSession
{
    private async Task ReplyAsync(ChatTarget target, IReadOnlyList<SessionEvent> leading, CancellationToken cancellation)
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
        if (Project?.ToPrompt() is { } instructions)
        {
            working.Insert(0, new ChatMessage(ChatRole.System, instructions));
            before++;
        }

        var reply = new Reply(entry, target, working, before, _time.GetTimestamp());
        try
        {
            using var client = await _clientFactory(target, cancellation);
            var options = ChatClientFactory.BuildOptions(target.Provider, target.Model, target.Level);
            await foreach (var e in ChatRunner.RunAsync(client, working, options, Tools, this, cancellationToken: cancellation))
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
        }
        finally
        {
            await CloseAsync(reply);
        }
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
                break;
            case Done done:
                entry.Status = done.Reason switch
                {
                    StopReason.Cancelled => EntryStatus.Cancelled,
                    StopReason.Error or StopReason.MaxIterations => EntryStatus.Failed,
                    _ => EntryStatus.Done,
                };
                break;
            case NewResponse:
                await StartNextResponseAsync(reply);
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
                SetStatus(entry, awaiting.CallId, ToolCallStatus.AwaitingApproval);
                break;
            case ToolCallRunning running:
                SetStatus(entry, running.CallId, ToolCallStatus.Running);
                break;
            case ToolCallFinished finished:
                await FinishCallAsync(entry, finished.CallId, finished.Status, finished.Output);
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

    private async Task FinishCallAsync(ChatEntry entry, string callId, ToolCallStatus status, string output)
    {
        if (entry.ToolCalls.FirstOrDefault(c => c.CallId == callId) is not { } call)
        {
            return;
        }

        call.Output = output;
        call.Status = status;
        await SaveAsync([new ToolResultEvent(_time.GetUtcNow(), entry.Id, callId, status, output)]);
    }

    private async Task FlushIfDueAsync(Reply reply)
    {
        if (_time.GetElapsedTime(reply.LastSave) >= FlushInterval)
        {
            await SaveAsync([TakeDelta(reply)]);
            reply.LastSave = _time.GetTimestamp();
        }
    }

    // The text of a reply is complete once its tool calls are known, so it is saved before the calls run.
    private Task EndResponseAsync(Reply reply)
    {
        var entry = reply.Entry;
        entry.Status = EntryStatus.Done;
        entry.At = _time.GetUtcNow();
        List<SessionEvent> events = [];
        if (reply.Text.Length > 0 || reply.Reasoning.Length > 0)
        {
            events.Add(TakeDelta(reply));
        }

        events.Add(new AssistantEndEvent(entry.At, entry.Id, entry.Status, entry.Error, entry.Usage));
        return SaveAsync(events);
    }

    private Task StartNextResponseAsync(Reply reply)
    {
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
        return SaveAsync([new AssistantStartEvent(startedAt, next.Id, next.Label)]);
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
        }

        var endedAt = _time.GetUtcNow();
        List<SessionEvent> closing = [];
        if (reply.Text.Length > 0 || reply.Reasoning.Length > 0)
        {
            closing.Add(TakeDelta(reply));
        }

        foreach (var call in entry.ToolCalls.Where(c => !c.IsFinished))
        {
            call.Status = ToolCallStatus.Cancelled;
            call.Output = ToolCallEntry.Unfinished(ToolCallStatus.Cancelled);
            closing.Add(new ToolResultEvent(endedAt, entry.Id, call.CallId, call.Status, call.Output));
        }

        entry.At = endedAt;
        closing.Add(new AssistantEndEvent(endedAt, entry.Id, entry.Status, entry.Error, entry.Usage));
        return SaveAsync(closing);
    }

    private AssistantDeltaEvent TakeDelta(Reply reply)
    {
        var delta = new AssistantDeltaEvent(_time.GetUtcNow(), reply.Entry.Id, reply.Text.ToString(), reply.Reasoning.ToString());
        reply.Text.Clear();
        reply.Reasoning.Clear();
        return delta;
    }

    private async Task SaveAsync(IReadOnlyCollection<SessionEvent> events)
    {
        if (_log is null)
        {
            return;
        }

        try
        {
            await _log.AppendAsync(events);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSaveFailed(ex);
        }
    }

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
