using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Folders;
using BlazeHarness.Core.Providers;
using BlazeHarness.Core.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace BlazeHarness.Core.Chats;

public sealed partial class ChatService
{
    private const int MaxCandidates = 5;

    internal SendToChatResolution ResolveSendToChat(string sourceId, string reference, string? folder, string message)
    {
        if (_store.Find(sourceId) is not { } source)
        {
            return Refuse("This chat no longer exists.");
        }

        if (string.Equals(reference.Trim(), SendToChatTool.NewChat, StringComparison.OrdinalIgnoreCase))
        {
            string? path;
            if (string.IsNullOrWhiteSpace(folder))
            {
                path = source.FolderPath;
            }
            else if (string.Equals(folder.Trim(), SendToChatTool.NoFolder, StringComparison.OrdinalIgnoreCase))
            {
                path = null;
            }
            else if (_folders?.Find(folder) is { } known)
            {
                path = known.Path;
            }
            else
            {
                return Refuse($"'{folder}' is not a folder that is open in the app.");
            }

            return new SendToChatResolution(new SendToChatPlan(null, null, path, SameFolder(source.FolderPath, path), message));
        }

        var destination = _store.Find(ChatReference.IdOf(reference));
        if (destination is null)
        {
            var title = reference.Trim();
            var matches = _store.List().Where(c => c.Id != sourceId && string.Equals(c.Title, title, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
            {
                return Refuse($"No chat has the reference or title '{title}'. Use a chat:<id> reference the user gave you, or '{SendToChatTool.NewChat}' to start a chat.");
            }

            if (matches.Count > 1)
            {
                var candidates = string.Join("; ", matches.Take(MaxCandidates).Select(c => $"\"{c.Title}\" ({ChatReference.Format(c.Id)}, {c.FolderPath ?? "no folder"})"));
                return Refuse($"{matches.Count} chats have the title '{title}': {candidates}. Send to one of them by its chat:<id> reference.");
            }

            destination = matches[0];
        }

        return destination.Id == sourceId
            ? Refuse("A chat can't send a message to itself.")
            : new SendToChatResolution(new SendToChatPlan(destination.Id, destination.Title, destination.FolderPath, SameFolder(source.FolderPath, destination.FolderPath), message));
    }

    internal async Task<ToolResult> SendToChatAsync(string sourceId, Chat sourceChat, SendToChatPlan plan, CancellationToken cancellationToken)
    {
        if (_store.Find(sourceId) is not { } sourceInfo)
        {
            return ToolResult.Error("This chat no longer exists.");
        }

        var hops = Math.Max(0, sourceChat.Entries.LastOrDefault(e => e.Role == ChatRole.User && e.Compaction is null)?.Source?.Hops ?? 0);
        if (hops >= ChatSource.MaxHops)
        {
            return ToolResult.Error($"Messages have passed through {hops} chats in a row without the user. Tell the user what you wanted to send instead.");
        }

        var existing = plan.ChatId is null ? null : _store.Find(plan.ChatId);
        if (plan.ChatId is not null && existing is null)
        {
            return ToolResult.Error("The other chat no longer exists.");
        }

        var remembered = existing ?? sourceInfo;
        if (PickTarget(remembered.ProviderId, remembered.ModelId, remembered.Level) is not { } target)
        {
            return ToolResult.Error("No model is set up to answer in the other chat. The user can add one in Settings.");
        }

        // Once a chat is created, nothing before the send may be cancelled, or an empty chat is left behind. A new chat already has its folder applied.
        var info = existing ?? await CreateAsync(plan.FolderPath, cancellationToken);
        if (await OpenAsync(info.Id, plan.Creates ? CancellationToken.None : cancellationToken) is not { } chat)
        {
            return ToolResult.Error("The other chat no longer exists.");
        }

        if (!plan.Creates)
        {
            await ApplyFolderAsync(info.Id, chat, cancellationToken);
        }

        var senderTitle = string.IsNullOrWhiteSpace(sourceInfo.Title) ? ChatReference.Format(sourceId) : sourceInfo.Title;
        var source = new ChatSource(sourceId, senderTitle, hops + 1);
        var text = source.Format(plan.Message);
        var where = string.Create(CultureInfo.InvariantCulture, $"the chat \"{info.Title}\" ({ChatReference.Format(info.Id)})");
        var created = plan.Creates ? "Created and sent to" : "Sent to";

        // A reply that ended between the two attempts leaves the chat idle again, so try once more.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var (started, reply) = chat.StartFromChat(text, source, target);
            if (started)
            {
                await RememberAsync(info.Id, target, plan.Message, cancellationToken: CancellationToken.None);
                _ = FinishSentReplyAsync(info.Id, chat, target, reply);
                return ToolResult.Ok($"{created} {where}. A turn started there. You will not see its reply; tell the user where you sent this.");
            }

            if (chat.QueueFromChat(text, source))
            {
                return ToolResult.Ok($"{created} {where}. It is busy, so the message is added when its reply ends. You will not see its reply; tell the user where you sent this.");
            }
        }

        return ToolResult.Error($"{where} could not take the message right now. Try again, or tell the user.");
    }

    private SendToChatTool? SendToChatFor(string id, Chat chat)
    {
        return _providers is null
            ? null
            : new SendToChatTool(
                (reference, folder, message) => ResolveSendToChat(id, reference, folder, message),
                (plan, cancellationToken) => SendToChatAsync(id, chat, plan, cancellationToken));
    }

    private async Task FinishSentReplyAsync(string id, Chat chat, ChatTarget target, Task reply)
    {
        try
        {
#pragma warning disable VSTHRD003 // The turn belongs to Chat, which runs it to the end on its own; this only waits to write the title.
            await reply;
#pragma warning restore VSTHRD003
            StartTitle(id, chat, target);
        }
        catch (Exception ex)
        {
            LogSentReplyFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A reply started by a message from another chat failed.")]
    private partial void LogSentReplyFailed(Exception ex);

    private ChatTarget? PickTarget(string? providerId, string? modelId, ThinkingLevel? level)
    {
        if (_providers is null)
        {
            return null;
        }

        var choices = _providers.Configured.SelectMany(p => p.Models.Select(m => (Provider: p, Model: m))).ToList();
        var config = _providers.Config;
        var pick = choices.Find(c => c.Provider.Id == providerId && c.Model.Id == modelId);
        if (pick.Provider is null)
        {
            pick = choices.Find(c => c.Provider.Id == config.DefaultProviderId && c.Model.Id == config.DefaultModelId);
        }

        if (pick.Provider is null)
        {
            if (choices.Count == 0)
            {
                return null;
            }

            pick = choices[0];
        }

        var chosen = level is { } wanted && pick.Model.Levels?.ContainsKey(wanted) == true ? wanted : pick.Model.DefaultLevel;
        return new ChatTarget(pick.Provider, pick.Model, chosen);
    }

    private static bool SameFolder(string? a, string? b)
    {
        return a is null ? b is null : b is not null && FolderPaths.Comparer.Equals(a, b);
    }

    private static SendToChatResolution Refuse(string error) => new(null, error);
}
