using BlazeHarness.Core.Agent;
using BlazeHarness.Core.Tools;
using Microsoft.Extensions.AI;
using System.Globalization;
using System.Text;

namespace BlazeHarness.Core.Attachments;

/// <summary>
/// Builds the message the model sees for a user message with attached files, and the one that shows it the pictures of tool calls.
/// </summary>
internal static class AttachmentMessage
{
    /// <summary>
    /// The most bytes of pictures shown to the model for one tool call.
    /// </summary>
    /// <remarks>
    /// A tool can return many pictures, and each is sent again with every request until compaction drops it. The ones past this
    /// are named instead, with their attachment ID, so the model can read them with <c>read_attachment</c>.
    /// </remarks>
    public const long MaxToolImageBytes = 20L * 1024 * 1024;

    /// <summary>
    /// The text that names the attachment in a tool picture's label.
    /// </summary>
    private const string AttachmentLabel = ", attachment ";

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
    /// <remarks>
    /// Each picture is labelled with its call and attachment ID. A picture larger than <see cref="ImageFiles.MaxViewableBytes"/>, or one past
    /// <see cref="MaxToolImageBytes"/> for its call, is named instead of shown.
    /// </remarks>
    /// <param name="calls">The calls of one response, with the pictures each produced.</param>
    /// <returns>The message, or null when no call produced a picture.</returns>
    public static ChatMessage? ForToolImages(IEnumerable<(string Name, string CallId, IReadOnlyList<ToolImage> Images)> calls)
    {
        return ToolImages(calls.Select(c => (c.Name, c.CallId, c.Images.Select(i => (i.Attachment, i.Size, (Func<DataContent?>)(() => i.Content))).ToList())));
    }

    /// <summary>
    /// Build the same message as <see cref="ForToolImages(IEnumerable{ValueTuple{string, string, IReadOnlyList{ToolImage}}})"/> from saved calls,
    /// reading the pictures from the chat's attachments.
    /// </summary>
    /// <param name="calls">The calls of one response.</param>
    /// <param name="store">The chat's attachments, or null when it has none.</param>
    /// <returns>The message, or null when no call produced a picture.</returns>
    public static ChatMessage? ForToolImages(IReadOnlyList<ToolCallEntry> calls, ChatAttachments? store)
    {
        return ToolImages(calls.Select(c => (c.Name, c.CallId, c.Images.Select(info => ((AttachmentInfo?)info, info.Size, (Func<DataContent?>)(() => Load(store, info)))).ToList())));
    }

    /// <summary>
    /// Get the label that names a picture of a tool call.
    /// </summary>
    /// <param name="name">The name of the tool.</param>
    /// <param name="callId">The ID of the call.</param>
    /// <param name="index">The place of the picture among the call's pictures, from 0.</param>
    /// <param name="count">How many pictures the call produced.</param>
    /// <param name="attachment">The chat's copy of the picture, or null when it has none.</param>
    /// <returns>The label, without brackets.</returns>
    public static string ToolImageLabel(string name, string callId, int index, int count, AttachmentInfo? attachment)
    {
        var picture = count > 1 ? string.Create(CultureInfo.InvariantCulture, $"Picture {index + 1} of {count}") : "Picture";
        return $"{picture} from {name} call {callId}{(attachment is null ? string.Empty : AttachmentLabel + attachment.Id)}";
    }

    /// <summary>
    /// Replace the pictures in a message from <see cref="ForToolImages(IEnumerable{ValueTuple{string, string, IReadOnlyList{ToolImage}}})"/> with notes.
    /// </summary>
    /// <remarks>
    /// Each labelled picture becomes one note that keeps its label and says how to read it again, so an old picture stops costing context.
    /// The note depends only on the message, so the request stays the same from one turn to the next.
    /// </remarks>
    /// <param name="message">The message.</param>
    /// <returns>A copy without the pictures, or null when the message is not a tool picture message or holds no picture.</returns>
    public static ChatMessage? WithoutToolImages(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!StepMessage.Is(message) || !message.Contents.Any(IsImage))
        {
            return null;
        }

        List<AIContent> contents = [];
        foreach (var content in message.Contents)
        {
            if (IsImage(content) && contents is [.., TextContent { Text: ['[', .. var label, ':', ']'] }])
            {
                contents[^1] = new TextContent(RemovedNote(label));
            }
            else
            {
                contents.Add(content);
            }
        }

        return new ChatMessage(message.Role, contents)
        {
            AuthorName = message.AuthorName,
            CreatedAt = message.CreatedAt,
            MessageId = message.MessageId,
            AdditionalProperties = message.AdditionalProperties,
            RawRepresentation = message.RawRepresentation,
        };
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

    private static ChatMessage? ToolImages(IEnumerable<(string Name, string CallId, List<(AttachmentInfo? Attachment, long Size, Func<DataContent?> Load)> Pictures)> calls)
    {
        List<AIContent> contents = [];
        foreach (var (name, callId, pictures) in calls)
        {
            var shown = 0L;
            for (var i = 0; i < pictures.Count; i++)
            {
                var (attachment, size, load) = pictures[i];
                var label = ToolImageLabel(name, callId, i, pictures.Count, attachment);
                if (size > ImageFiles.MaxViewableBytes)
                {
                    contents.Add(new TextContent(string.Create(
                        CultureInfo.InvariantCulture,
                        $"[{label} was not shown: at {FileSize.Format(size)} it is larger than the {ImageFiles.MaxViewableBytes / (1024 * 1024)} MB a picture can be.]")));
                }
                else if (shown + size > MaxToolImageBytes)
                {
                    var hint = attachment is null ? string.Empty : $" Read it with read_attachment and the id {attachment.Id} if you need it.";
                    contents.Add(new TextContent(string.Create(
                        CultureInfo.InvariantCulture,
                        $"[{label} was not shown, to keep the pictures of one call under {MaxToolImageBytes / (1024 * 1024)} MB.{hint}]")));
                }
                else if (load() is { } data)
                {
                    shown += size;
                    contents.Add(new TextContent($"[{label}:]"));
                    contents.Add(data);
                }
                else
                {
                    contents.Add(new TextContent($"[{label} is no longer available.]"));
                }
            }
        }

        return contents.Count == 0 ? null : StepMessage.Create(contents);
    }

    private static string RemovedNote(string label)
    {
        var at = label.LastIndexOf(AttachmentLabel, StringComparison.Ordinal);
        return at < 0
            ? $"[{label} was removed to save context.]"
            : $"[{label} was removed to save context. Read it with read_attachment and the id {label[(at + AttachmentLabel.Length)..]} if you need it.]";
    }

    private static DataContent? Load(ChatAttachments? store, AttachmentInfo info)
    {
        return store?.Find(info.Id) is { } file ? new DataContent(File.ReadAllBytes(file.Path), info.MediaType) : null;
    }

    private static bool IsImage(AIContent content) => content is DataContent { MediaType: var type } && type.StartsWith("image/", StringComparison.Ordinal);

    private static string ReadText(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd().TrimEnd('\n', '\r');
    }
}
