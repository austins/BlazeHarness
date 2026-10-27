using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Attachments;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace BlazeHarness.Core.Sessions;

/// <summary>
/// Writes a chat as a readable Markdown transcript, with tool calls and thinking collapsed.
/// </summary>
internal static class SessionMarkdown
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Write(SessionInfo info, IReadOnlyList<SessionEvent> events)
    {
        var text = new StringBuilder();
        text.Append("# ").Append(info.Title.Length == 0 ? "Untitled chat" : OneLine(info.Title)).Append("\n\n");
        text.Append("Started ").Append(info.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append(" UTC\n\n");

        foreach (var turn in Replay(events))
        {
            if (turn.Summary is not null)
            {
                text.Append("---\n\n*Earlier messages were summarized at this point.*\n\n");
                Details(text, "Summary", turn.Summary);
                text.Append("---\n\n");
            }
            else if (turn.IsUser)
            {
                text.Append("## You\n\n");
                if (turn.Text.Length > 0)
                {
                    text.Append(turn.Text).Append("\n\n");
                }

                if (turn.Attachments.Count > 0)
                {
                    text.Append("*Attached: ").Append(string.Join(", ", turn.Attachments.Select(a => a.File.Name))).Append("*\n\n");
                }
            }
            else
            {
                WriteReply(text, turn);
            }
        }

        return text.ToString().TrimEnd('\n') + "\n";
    }

    private static void WriteReply(StringBuilder text, Turn turn)
    {
        text.Append("## Assistant");
        if (!string.IsNullOrEmpty(turn.Label))
        {
            text.Append(" · ").Append(OneLine(turn.Label));
        }

        text.Append("\n\n");
        if (turn.Reasoning.Length > 0)
        {
            Details(text, "Thinking", turn.Reasoning);
        }

        if (turn.Text.Length > 0)
        {
            text.Append(turn.Text).Append("\n\n");
        }

        foreach (var call in turn.Calls)
        {
            text.Append("<details>\n<summary><code>").Append(WebUtility.HtmlEncode(call.Name)).Append("</code>");
            if (call.Summary.Length > 0)
            {
                text.Append(' ').Append(WebUtility.HtmlEncode(OneLine(call.Summary)));
            }

            text.Append(" · ").Append(StatusText(call.Status)).Append("</summary>\n\n");
            text.Append("Arguments:\n\n").Append(Fence(Arguments(call.ArgumentsJson), "json")).Append("\n\n");
            if (call.Output.Length > 0)
            {
                text.Append("Output:\n\n").Append(Fence(call.Output, "text")).Append("\n\n");
            }

            text.Append("</details>\n\n");
        }

        switch (turn.Status)
        {
            case EntryStatus.Failed:
                text.Append("> The reply failed: ").Append(OneLine(turn.Error ?? "unknown error")).Append("\n\n");
                break;
            case EntryStatus.Cancelled:
                text.Append("> The reply was stopped.\n\n");
                break;
            case EntryStatus.Interrupted:
                text.Append("> The reply was cut off.\n\n");
                break;
            default:
                break;
        }
    }

    private static void Details(StringBuilder text, string summary, string body)
    {
        text.Append("<details>\n<summary>").Append(summary).Append("</summary>\n\n").Append(body).Append("\n\n</details>\n\n");
    }

    // A fence longer than any run of backticks inside the text, so the text can't end the block early.
    private static string Fence(string content, string language)
    {
        var longest = 0;
        var run = 0;
        foreach (var c in content)
        {
            run = c == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        var fence = new string('`', Math.Max(3, longest + 1));
        return fence + language + "\n" + content.TrimEnd('\n') + "\n" + fence;
    }

    private static string Arguments(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, Pretty);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static string OneLine(string text) => text.ReplaceLineEndings(" ").Trim();

    private static string StatusText(ToolCallStatus status) => status switch
    {
        ToolCallStatus.Succeeded => "Done",
        ToolCallStatus.Failed => "Failed",
        ToolCallStatus.Denied => "Denied",
        ToolCallStatus.Cancelled => "Stopped",
        _ => "Interrupted",
    };

    // Follows the same rules as restoring a chat: a rewind drops the messages after it and a compaction marker sits before the first message it kept.
    private static List<Turn> Replay(IReadOnlyList<SessionEvent> events)
    {
        var turns = new List<Turn>();
        Turn? Find(Guid id) => turns.Find(t => t.Id == id);

        foreach (var e in events)
        {
            switch (e)
            {
                case UserMessageEvent user:
                    turns.Add(new Turn(user.EntryId, isUser: true) { Text = user.Text, Attachments = user.Attachments ?? [] });
                    break;
                case AssistantStartEvent start:
                    turns.Add(new Turn(start.EntryId, isUser: false) { Label = start.Label });
                    break;
                case AssistantDeltaEvent delta when Find(delta.EntryId) is { } target:
                    target.Text += delta.Text;
                    target.Reasoning += delta.Reasoning;
                    break;
                case AssistantEndEvent end when Find(end.EntryId) is { } target:
                    target.Status = end.Status;
                    target.Error = end.Error;
                    break;
                case ToolCallEvent call when Find(call.EntryId) is { } target:
                    target.Calls.Add(new Call(call.CallId, call.Name, call.ArgumentsJson, call.Summary));
                    break;
                case ToolResultEvent result when Find(result.EntryId)?.Calls.Find(c => c.CallId == result.CallId) is { } finished:
                    finished.Status = result.Status;
                    finished.Output = result.Output;
                    break;
                case CompactionEvent compaction when turns.FindIndex(t => t.Id == compaction.FirstKeptEntryId) is var at and >= 0:
                    turns.Insert(at, new Turn(compaction.EntryId, isUser: false) { Summary = compaction.Summary });
                    break;
                case RewindEvent rewind:
                    var keep = rewind.ToEntryId == Guid.Empty ? -1 : turns.FindIndex(t => t.Id == rewind.ToEntryId);
                    if (keep >= 0 || rewind.ToEntryId == Guid.Empty)
                    {
                        turns.RemoveRange(keep + 1, turns.Count - keep - 1);
                    }

                    break;
                default:
                    break;
            }
        }

        return turns;
    }

    private sealed class Turn(Guid id, bool isUser)
    {
        public Guid Id { get; } = id;

        public bool IsUser { get; } = isUser;

        public string Text { get; set; } = string.Empty;

        public string Reasoning { get; set; } = string.Empty;

        public string? Label { get; init; }

        public string? Summary { get; init; }

        public EntryStatus Status { get; set; } = EntryStatus.Interrupted;

        public string? Error { get; set; }

        public IReadOnlyList<SentAttachment> Attachments { get; init; } = [];

        public List<Call> Calls { get; } = [];
    }

    private sealed class Call(string callId, string name, string argumentsJson, string summary)
    {
        public string CallId { get; } = callId;

        public string Name { get; } = name;

        public string ArgumentsJson { get; } = argumentsJson;

        public string Summary { get; } = summary;

        public ToolCallStatus Status { get; set; } = ToolCallStatus.Interrupted;

        public string Output { get; set; } = string.Empty;
    }
}
