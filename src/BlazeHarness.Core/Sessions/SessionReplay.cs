using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Compaction;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Sessions;

/// <summary>
/// Rebuilds the messages of a chat from its saved events, so restoring a chat and exporting it follow the same rules.
/// </summary>
internal static class SessionReplay
{
    /// <summary>
    /// Create the entry that marks where a compaction summary begins to stand in for older messages.
    /// </summary>
    /// <param name="id">The id of the marker.</param>
    /// <param name="at">When the compaction happened.</param>
    /// <param name="info">The summary and token counts.</param>
    /// <returns>An entry with an empty text and <see cref="ChatEntry.Compaction"/> set.</returns>
    public static ChatEntry NewMarker(Guid id, DateTimeOffset at, CompactionInfo info)
    {
        return new ChatEntry(ChatRole.System, id: id) { Status = EntryStatus.Done, At = at, Compaction = info };
    }

    /// <summary>
    /// Replay events into messages.
    /// </summary>
    /// <remarks>
    /// A rewind drops the messages after the one it names, and a compaction marker sits before the first message it kept.
    /// A reply with no end event stays <see cref="EntryStatus.Interrupted"/>. Events about an unknown message are ignored.
    /// </remarks>
    /// <param name="events">The events, in the order they were written.</param>
    /// <returns>The messages that remain, in order.</returns>
    public static List<ChatEntry> Replay(IReadOnlyList<SessionEvent> events)
    {
        List<ChatEntry> entries = [];
        Dictionary<Guid, ChatEntry> byId = [];

        void Add(ChatEntry entry, int? at = null)
        {
            entries.Insert(at ?? entries.Count, entry);
            byId[entry.Id] = entry;
        }

        foreach (var e in events)
        {
            switch (e)
            {
                case UserMessageEvent user:
                    Add(new ChatEntry(ChatRole.User, user.Text, user.EntryId) { Status = EntryStatus.Done, At = user.At, Attachments = user.Attachments ?? [] });
                    break;
                case AssistantStartEvent start:
                    Add(new ChatEntry(ChatRole.Assistant, id: start.EntryId) { Status = EntryStatus.Interrupted, Label = start.Label, At = start.At });
                    break;
                case AssistantDeltaEvent delta when byId.TryGetValue(delta.EntryId, out var target):
                    target.AppendText(delta.Text);
                    target.AppendReasoning(delta.Reasoning);
                    break;
                case AssistantEndEvent end when byId.TryGetValue(end.EntryId, out var target):
                    target.Status = end.Status;
                    target.Error = end.Error;
                    target.Usage = end.Usage;
                    target.At = end.At;
                    break;
                case ToolCallEvent call when byId.TryGetValue(call.EntryId, out var target):
                    target.AddToolCall(new ToolCallEntry(call.CallId, call.Name, call.ArgumentsJson, call.Summary));
                    break;
                case ToolResultEvent result when byId.TryGetValue(result.EntryId, out var target) && target.ToolCalls.FirstOrDefault(c => c.CallId == result.CallId) is { } finished:
                    finished.Status = result.Status;
                    finished.Output = result.Output;
                    break;
                case CompactionEvent compaction when entries.FindIndex(x => x.Id == compaction.FirstKeptEntryId) is var at and >= 0:
                    Add(NewMarker(compaction.EntryId, compaction.At, new CompactionInfo(compaction.Summary, compaction.TokensBefore, compaction.TokensAfter)), at);
                    break;
                case RewindEvent rewind:
                    var keep = rewind.ToEntryId == Guid.Empty ? -1 : entries.FindIndex(x => x.Id == rewind.ToEntryId);
                    if (keep >= 0 || rewind.ToEntryId == Guid.Empty)
                    {
                        foreach (var dropped in entries.Skip(keep + 1))
                        {
                            byId.Remove(dropped.Id);
                        }

                        entries.RemoveRange(keep + 1, entries.Count - keep - 1);
                    }

                    break;
                default:
                    break;
            }
        }

        MarkInterrupted(entries);
        return entries;
    }

    private static void MarkInterrupted(List<ChatEntry> entries)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var dangling = false;
            foreach (var call in entry.ToolCalls.Where(c => !c.IsFinished))
            {
                call.Status = ToolCallStatus.Interrupted;
                call.Output = ToolCallEntry.Unfinished(ToolCallStatus.Interrupted);
                dangling = true;
            }

            if (entry.Status == EntryStatus.Done && (dangling || (i == entries.Count - 1 && entry.ToolCalls.Count > 0)))
            {
                entry.Status = EntryStatus.Interrupted;
            }
        }
    }
}
