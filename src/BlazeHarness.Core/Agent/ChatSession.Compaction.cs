using BlazeHarness.Core.Compaction;
using BlazeHarness.Core.Sessions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace BlazeHarness.Core.Agent;

public sealed partial class ChatSession
{
    // The summary that replaces older messages and the history index where the messages that are sent as they were begin. Guarded by _gate.
    private (string Summary, int KeptFrom)? _compaction;

    // The newest entry when the conversation was last compacted. Its prompt size, and that of the ones before it, was reported for a request that was not compacted.
    private ChatEntry? _compactedAfter;

    /// <summary>
    /// Get whether the conversation can be compacted by the user now.
    /// </summary>
    /// <returns>True when no reply is running and there are at least two messages.</returns>
    public bool CanCompact
    {
        get
        {
            lock (_gate)
            {
                return _stop is null && _history.Count >= 2;
            }
        }
    }

    /// <summary>
    /// Get how full the context is for a model.
    /// </summary>
    /// <remarks>
    /// The size is the prompt size the server reported for the last reply plus an estimate for what came after.
    /// Without a reported size, or right after a compaction, it is an estimate of the whole request.
    /// </remarks>
    /// <param name="target">The model the next message would go to.</param>
    /// <returns>The size of the next request, the limits, and the cache hit rate when the server reports one.</returns>
    public ContextStatus GetContextStatus(ChatTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        lock (_gate)
        {
            var lastUsage = _entries.FindLastIndex(e => e.Usage is { PromptTokens: > 0 });
            var stale = lastUsage >= 0 && (lastUsage < _entries.FindLastIndex(e => e.Compaction is not null) || (_compactedAfter is not null && lastUsage <= _entries.IndexOf(_compactedAfter)));
            long tokens;
            if (lastUsage >= 0 && !stale)
            {
                var last = _entries[lastUsage];
                tokens = last.Usage!.PromptTokens + TokenEstimate.Of(_history.Skip(last.HistoryIndex));
            }
            else
            {
                tokens = TokenEstimate.Of(SystemPromptText()) + TokenEstimate.Of(_history.Skip(_compaction?.KeptFrom ?? 0));
                tokens += _compaction is { } compaction ? TokenEstimate.Of(ConversationContext.SummaryMessage(compaction.Summary)) : 0;
            }

            double? cache = lastUsage >= 0 && _entries.Exists(e => e.Usage is { CachedTokens: > 0 })
                ? (double)_entries[lastUsage].Usage!.CachedTokens / _entries[lastUsage].Usage!.PromptTokens
                : null;
            return new ContextStatus(tokens, lastUsage < 0 || stale, target.Model.ContextWindow, ContextBudget.Limit(target.Model), cache);
        }
    }

    /// <summary>
    /// Replace older messages with a summary now, instead of waiting for the context to fill up.
    /// </summary>
    /// <remarks>
    /// The newest user message and what follows it are kept as they were, unless that part is itself large.
    /// The older messages stay in the conversation and in the log, with a marker where the summary begins to stand in for them.
    /// </remarks>
    /// <param name="target">The model that writes the summary.</param>
    /// <returns>True when the conversation was compacted; false when a reply is running, there is nothing older to summarize, or it was stopped.</returns>
    /// <exception cref="InvalidOperationException">The model could not write the summary.</exception>
    public async Task<bool> CompactAsync(ChatTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        using var run = new CancellationTokenSource();
        List<ChatMessage> working;
        (string Summary, int KeptFrom)? state;
        lock (_gate)
        {
            if (_stop is not null || _history.Count < 2)
            {
                return false;
            }

            _stop = run.Cancel;
            working = [.. _history];
            state = _compaction;
        }

        RaiseChanged();
        try
        {
            using var client = await _clientFactory(target, run.Token);
            var context = new ConversationContext(ContextBudget.Limit(target.Model), ModelSummarizer.For(client, target), 0, state?.Summary, state?.KeptFrom ?? 0);
            if (await context.CompactAsync(working, manual: true, run.Token) is not { } result)
            {
                return false;
            }

            return await ApplyCompactionAsync(result);
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            Volatile.Write(ref _stop, null);
            RaiseChanged();
        }
    }

    private string SystemPromptText()
    {
        return string.Join(Environment.NewLine, new[] { BasePrompt, Project?.ToPrompt(), Tools is null ? null : Skills?.ToPrompt() }.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private ConversationContext CreateContext(IChatClient client, ChatTarget target, int leadingMessages)
    {
        (string Summary, int KeptFrom)? state;
        lock (_gate)
        {
            state = _compaction;
        }

        return new ConversationContext(ContextBudget.Limit(target.Model), ModelSummarizer.For(client, target), leadingMessages, state?.Summary, state?.KeptFrom ?? 0);
    }

    // Must be called with the gate held, after the entries or the history changed.
    private void RefreshCompaction()
    {
        _compaction = null;
        var marker = _entries.FindLastIndex(e => e.Compaction is not null);
        if (marker >= 0)
        {
            var next = _entries.Skip(marker + 1).FirstOrDefault(e => e.Compaction is null);
            _compaction = (_entries[marker].Compaction!.Summary, next?.HistoryIndex ?? _history.Count);
        }
    }

    private async Task<bool> ApplyCompactionAsync(ContextCompacted compacted)
    {
        var at = _time.GetUtcNow();
        CompactionEvent saved;
        lock (_gate)
        {
            var kept = _entries.Find(e => e.Compaction is null && e.HistoryIndex == compacted.FirstKept);
            if (kept is null)
            {
                return false;
            }

            var marker = SessionReplay.NewMarker(Guid.NewGuid(), at, new CompactionInfo(compacted.Summary, compacted.TokensBefore, compacted.TokensAfter));
            _entries.Insert(_entries.IndexOf(kept), marker);
            _compactedAfter = _entries[^1];
            RefreshCompaction();
            Publish();
            saved = new CompactionEvent(at, marker.Id, kept.Id, compacted.Summary, compacted.TokensBefore, compacted.TokensAfter);
        }

        await SaveAsync([saved]);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not compact the conversation, so it was sent in full: {Reason}")]
    private partial void LogCompactionFailed(string reason);
}
