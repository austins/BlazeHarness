using BlazeHarness.Core.Agent.Events;
using BlazeHarness.Core.Chats;
using BlazeHarness.Core.Chats.Events;
using BlazeHarness.Core.Compaction;
using BlazeHarness.Core.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace BlazeHarness.Core.Agent;

public sealed partial class Chat
{
    // The summary that replaces older messages (null when only tool output was trimmed), the history index where the messages that are sent as they were begin,
    // and the history index before which large tool output is a stub. Null when the conversation has no marker. Guarded by _gate.
    private (string? Summary, int KeptFrom, int StubbedBefore)? _compaction;

    // The newest entry when the conversation was last compacted. Its prompt size, and that of the ones before it, was reported for a request that was not compacted.
    private ChatEntry? _compactedAfter;

    // The estimated size of the tool definitions the last reply sent, used while no reported size covers them. Read with Interlocked.
    private long _toolTokens;

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
    /// Without a reported size, or right after a compaction or a trim of old tool output, it is an estimate of the whole request.
    /// </remarks>
    /// <param name="target">The model the next message would go to.</param>
    /// <returns>The size of the next request, the limits, and the cache hit rate when the server reports one.</returns>
    public ContextStatus GetContextStatus(ChatTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        lock (_gate)
        {
            var lastUsage = _entries.FindLastIndex(e => e.Usage is { PromptTokens: > 0 });
            var stale = UsageIsStale(lastUsage);

            // The same context a reply builds, so the figure follows what the request really holds, including the stubs.
            var setup = Volatile.Read(ref _setup);
            var prompt = SystemPromptText(setup);
            var leading = prompt.Length > 0 ? 1 : 0;
            List<ChatMessage> messages = leading > 0 ? [new ChatMessage(ChatRole.System, prompt), .. _history] : [.. _history];
            var context = NewContext(ContextBudget.Limit(target.Model), new ExtractiveSummarizer(), leading, _compaction, ToolTokens(setup.Tools));
            if (lastUsage >= 0 && !stale)
            {
                context.RecordUsage(_entries[lastUsage].Usage!.PromptTokens, leading + _entries[lastUsage].HistoryIndex);
            }

            // During a reply the history holds only what came before it, so the size the server reported for the reply's last request is used as it is.
            var tokens = lastUsage >= 0 && !stale && leading + _entries[lastUsage].HistoryIndex > messages.Count
                ? _entries[lastUsage].Usage!.PromptTokens
                : context.Size(messages).Tokens;

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
        (string? Summary, int KeptFrom, int StubbedBefore)? state;
        lock (_gate)
        {
            if (_stop is not null || _closed || _history.Count < 2)
            {
                return false;
            }

            _stop = run.Cancel;
            working = [.. _history];
            state = _compaction;
        }

        RaiseChanged();
        IChatClient? client = null;
        try
        {
            ISummarizer summarizer;
            if (UsesExtractiveCompaction)
            {
                summarizer = new ExtractiveSummarizer();
            }
            else
            {
                client = await _clientFactory(target, run.Token);
                summarizer = ModelSummarizer.For(client, target, _time);
            }

            var setup = Volatile.Read(ref _setup);
            var context = NewContext(ContextBudget.Limit(target.Model), summarizer, 0, state, TokenEstimate.Of(SystemPromptText(setup)) + ToolTokens(setup.Tools));
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
            client?.Dispose();
            Volatile.Write(ref _stop, null);
            RaiseChanged();
        }
    }

    private bool UsesExtractiveCompaction => CompactionModeSource?.Invoke() == CompactionMode.Extractive;

    private string SystemPromptText(ToolSetup setup)
    {
        return string.Join(Environment.NewLine, new[] { setup.BasePrompt, Project?.ToPrompt(), setup.Tools is null ? null : Skills?.ToPrompt() }.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private long ToolTokens(ToolSet? current)
    {
        var last = Interlocked.Read(ref _toolTokens);
        return last > 0 ? last : current is { } tools ? TokenEstimate.OfTools(tools.Tools) : 0;
    }

    private ConversationContext CreateContext(IChatClient client, ChatTarget target, int leadingMessages, long toolTokens)
    {
        (string? Summary, int KeptFrom, int StubbedBefore)? state;
        (long PromptTokens, int HistoryIndex)? usage = null;
        lock (_gate)
        {
            state = _compaction;
            var lastUsage = _entries.FindLastIndex(e => e.Usage is { PromptTokens: > 0 });
            if (lastUsage >= 0 && !UsageIsStale(lastUsage))
            {
                usage = (_entries[lastUsage].Usage!.PromptTokens, _entries[lastUsage].HistoryIndex);
            }
        }

        var context = NewContext(ContextBudget.Limit(target.Model), UsesExtractiveCompaction ? new ExtractiveSummarizer() : ModelSummarizer.For(client, target, _time), leadingMessages, state, toolTokens);

        // The size the server reported for the last request is more exact than an estimate, even at the start of a reply.
        if (usage is { } reported)
        {
            context.RecordUsage(reported.PromptTokens, leadingMessages + reported.HistoryIndex);
        }

        return context;
    }

    private static ConversationContext NewContext(int limit, ISummarizer summarizer, int leadingMessages, (string? Summary, int KeptFrom, int StubbedBefore)? state, long overheadTokens)
    {
        return new ConversationContext(limit, summarizer, leadingMessages, state?.Summary, state?.KeptFrom ?? 0, overheadTokens, state?.StubbedBefore ?? 0);
    }

    // Must be called with the gate held. A reported size from before a compaction or a trim describes a request that no longer exists.
    private bool UsageIsStale(int lastUsage)
    {
        return lastUsage >= 0 && (lastUsage < _entries.FindLastIndex(e => e.Compaction is not null) || (_compactedAfter is not null && lastUsage <= _entries.IndexOf(_compactedAfter)));
    }

    // Must be called with the gate held, after the entries or the history changed.
    private void RefreshCompaction()
    {
        _compaction = null;
        var summary = _entries.FindLastIndex(e => e.Compaction is { OutputTrimmed: false });
        var trim = _entries.FindLastIndex(e => e.Compaction is { OutputTrimmed: true });
        if (summary >= 0 || trim >= 0)
        {
            _compaction = (summary >= 0 ? _entries[summary].Compaction!.Summary : null, summary >= 0 ? NextHistoryIndex(summary) : 0, trim >= 0 ? NextHistoryIndex(trim) : 0);
        }
    }

    // Must be called with the gate held. The history index of the first message after a marker.
    private int NextHistoryIndex(int marker)
    {
        return _entries.Skip(marker + 1).FirstOrDefault(e => e.Compaction is null)?.HistoryIndex ?? _history.Count;
    }

    private async Task<bool> ApplyTrimAsync(ContextTrimmed trimmed)
    {
        var at = _time.GetUtcNow();
        ContextTrimEvent saved;
        lock (_gate)
        {
            var next = _entries.Find(e => e.Compaction is null && e.HistoryIndex == trimmed.TrimmedBefore);
            if (next is null)
            {
                return false;
            }

            var marker = ChatReplay.NewMarker(Guid.NewGuid(), at, new CompactionInfo(string.Empty, trimmed.TokensBefore, trimmed.TokensAfter, OutputTrimmed: true));
            _entries.Insert(_entries.IndexOf(next), marker);
            _compactedAfter = _entries[^1];
            RefreshCompaction();
            Publish();
            saved = new ContextTrimEvent(at, marker.Id, next.Id, trimmed.TokensBefore, trimmed.TokensAfter);
        }

        await SaveAsync([saved]);
        return true;
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

            var marker = ChatReplay.NewMarker(Guid.NewGuid(), at, new CompactionInfo(compacted.Summary, compacted.TokensBefore, compacted.TokensAfter));
            _entries.Insert(_entries.IndexOf(kept), marker);
            _compactedAfter = _entries[^1];
            RefreshCompaction();
            Publish();
            saved = new CompactionEvent(at, marker.Id, kept.Id, compacted.Summary, compacted.TokensBefore, compacted.TokensAfter);
        }

        await SaveAsync([saved]);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not compact the conversation, so it was sent in full.")]
    private partial void LogCompactionFailed();
}
