using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Agent.Questions;
using BlazeHarness.Core.Attachments;
using BlazeHarness.Core.Chats.Events;
using Microsoft.Extensions.AI;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace BlazeHarness.Core.Chats.Export;

/// <summary>
/// Writes a chat as a readable Markdown transcript, with tool calls and thinking collapsed.
/// </summary>
internal static class ChatMarkdown
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Write(ChatInfo info, IReadOnlyList<ChatEvent> events)
    {
        var text = new StringBuilder();
        text.Append("# ").Append(info.Title.Length == 0 ? "Untitled chat" : OneLine(info.Title)).Append("\n\n");
        text.Append("Started ").Append(info.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append(" UTC\n\n");

        foreach (var turn in ChatReplay.Replay(events))
        {
            if (turn.Compaction is { OutputTrimmed: true })
            {
                text.Append("---\n\n*Older tool output was trimmed at this point to save context.*\n\n---\n\n");
            }
            else if (turn.Compaction is { } compaction)
            {
                text.Append("---\n\n*Earlier messages were summarized at this point.*\n\n");
                Details(text, "Summary", compaction.Summary);
                text.Append("---\n\n");
            }
            else if (turn.Role == ChatRole.User)
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

    private static void WriteReply(StringBuilder text, ChatEntry turn)
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

        foreach (var call in turn.ToolCalls)
        {
            if (call.Question is { } question)
            {
                WriteQuestion(text, call, question);
                continue;
            }

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

    /// <summary>
    /// Write a user question and its answer as visible quotations.
    /// </summary>
    /// <remarks>They are the user's side of the conversation, so they stay in the open rather than collapsed.</remarks>
    /// <param name="text">The Markdown output.</param>
    /// <param name="call">The question's tool call and answer.</param>
    /// <param name="question">The question and its options.</param>
    private static void WriteQuestion(StringBuilder text, ToolCallEntry call, UserQuestion question)
    {
        Quote(text, "**The assistant asked:** " + question.Question);
        if (question.Options.Count > 0)
        {
            text.Append(">\n");
            foreach (var option in question.Options)
            {
                Quote(text, "- " + OneLine(option.Label) + (option.Description is null ? string.Empty : ": " + OneLine(option.Description)));
            }
        }

        text.Append(">\n");
        var answer = call.Status == ToolCallStatus.Succeeded && UserAnswer.TryRead(call.Output) is { } read ? read.Display : null;
        Quote(text, answer is not null ? "**Your answer:** " + answer : "*No answer: " + OneLine(call.Output.Length > 0 ? call.Output : "the question was not answered.") + "*");
        text.Append('\n');
    }

    private static void Quote(StringBuilder text, string block)
    {
        foreach (var line in block.ReplaceLineEndings("\n").Split('\n'))
        {
            text.Append("> ").Append(line).Append('\n');
        }
    }

    private static void Details(StringBuilder text, string summary, string body)
    {
        text.Append("<details>\n<summary>").Append(summary).Append("</summary>\n\n").Append(body).Append("\n\n</details>\n\n");
    }

    /// <summary>
    /// Wrap content in a code fence longer than any backtick run inside it.
    /// </summary>
    /// <remarks>The content cannot end the block early.</remarks>
    /// <param name="content">The code block's content.</param>
    /// <param name="language">The code fence's language label.</param>
    private static string Fence(string content, string language)
    {
        var fence = new string('`', Math.Max(3, AttachmentMessage.LongestBacktickRun(content) + 1));
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
}
