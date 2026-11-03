using BlazeHarness.Core.Agent;
using Microsoft.Extensions.AI;
using System.Globalization;
using System.Text;

namespace BlazeHarness.Core.Attachments;

/// <summary>
/// Builds the message the model sees for a user message with attached files.
/// </summary>
internal static class AttachmentMessage
{
    public static ChatMessage Build(string text, IReadOnlyList<SentAttachment> attachments, ChatAttachments? store)
    {
        if (attachments.Count == 0)
        {
            return new ChatMessage(ChatRole.User, text);
        }

        List<string> parts = [];
        if (text.Length > 0)
        {
            parts.Add(text);
        }

        List<AIContent> images = [];
        foreach (var attachment in attachments)
        {
            var info = attachment.File;
            if (store?.Find(info.Id) is not { } file)
            {
                parts.Add($"[Attached file '{info.Name}' is no longer available.]");
                continue;
            }

            switch (attachment.Delivery)
            {
                case AttachmentDelivery.Image:
                    parts.Add($"[Attached image: {info.Name}, id {info.Id}, path {file.Path}]");
                    images.Add(new DataContent(File.ReadAllBytes(file.Path), info.MediaType));
                    break;
                case AttachmentDelivery.Inline:
                    var content = ReadText(file.Path);
                    var fence = new string('`', Math.Max(3, LongestBacktickRun(content) + 1));
                    parts.Add($"Attached file {info.Name}:\n{fence}\n{content}\n{fence}");
                    break;
                default:
                    parts.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"[Attached file: {info.Name}, id {info.Id}, {FileSize.Format(info.Size)}, {info.Lines} lines. It is too large to include here. Read it with the read_attachment tool.]"));
                    break;
            }
        }

        return new ChatMessage(ChatRole.User, [new TextContent(string.Join("\n\n", parts)), .. images]);
    }

    /// <summary>
    /// Build the message that shows the model the pictures tool calls produced, sent after the results of the calls.
    /// </summary>
    /// <param name="calls">The calls of one response, with the picture each produced or null.</param>
    /// <returns>The message, or null when no call produced a picture.</returns>
    public static ChatMessage? ForToolImages(IEnumerable<(string Name, string CallId, DataContent? Image)> calls)
    {
        List<AIContent> contents = [];
        foreach (var (name, callId, image) in calls)
        {
            if (image is not null)
            {
                contents.Add(new TextContent(ToolImageLabel(name, callId)));
                contents.Add(image);
            }
        }

        return contents.Count == 0 ? null : new ChatMessage(ChatRole.User, contents);
    }

    /// <summary>
    /// Build the same message as <see cref="ForToolImages(IEnumerable{ValueTuple{string, string, DataContent}})"/> from saved calls, reading the pictures from the chat's attachments.
    /// </summary>
    /// <param name="calls">The calls of one response.</param>
    /// <param name="store">The chat's attachments, or null when it has none.</param>
    /// <returns>The message, or null when no call produced a picture.</returns>
    public static ChatMessage? ForToolImages(IReadOnlyList<ToolCallEntry> calls, ChatAttachments? store)
    {
        List<AIContent> contents = [];
        foreach (var call in calls)
        {
            if (call.Image is not { } info)
            {
                continue;
            }

            if (store?.Find(info.Id) is { } file)
            {
                contents.Add(new TextContent(ToolImageLabel(call.Name, call.CallId)));
                contents.Add(new DataContent(File.ReadAllBytes(file.Path), info.MediaType));
            }
            else
            {
                contents.Add(new TextContent($"[The picture from {call.Name} call {call.CallId} is no longer available.]"));
            }
        }

        return contents.Count == 0 ? null : new ChatMessage(ChatRole.User, contents);
    }

    public static void OmitImages(List<ChatMessage> messages)
    {
        for (var i = 0; i < messages.Count; i++)
        {
            if (messages[i].Contents.Any(IsImage))
            {
                messages[i] = new ChatMessage(
                    messages[i].Role,
                    [.. messages[i].Contents.Select(c => IsImage(c) ? new TextContent("[Image omitted: this model can't view images.]") : c)]);
            }
        }
    }

    internal static int LongestBacktickRun(string text)
    {
        var longest = 0;
        var run = 0;
        foreach (var c in text)
        {
            run = c == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        return longest;
    }

    private static string ToolImageLabel(string name, string callId) => $"[Picture from {name} call {callId}:]";

    private static bool IsImage(AIContent content) => content is DataContent { MediaType: var type } && type.StartsWith("image/", StringComparison.Ordinal);

    private static string ReadText(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd().TrimEnd('\n', '\r');
    }
}
