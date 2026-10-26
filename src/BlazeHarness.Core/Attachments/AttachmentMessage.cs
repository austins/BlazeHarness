using Microsoft.Extensions.AI;
using System.Globalization;
using System.Text;

namespace BlazeHarness.Core.Attachments;

/// <summary>
/// Builds the message the model sees for a user message with attached files.
/// </summary>
internal static class AttachmentMessage
{
    public static ChatMessage Build(string text, IReadOnlyList<SentAttachment> attachments, SessionAttachments? store)
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
                    parts.Add($"[Attached image: {info.Name}, id {info.Id}]");
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

    private static bool IsImage(AIContent content) => content is DataContent { MediaType: var type } && type.StartsWith("image/", StringComparison.Ordinal);

    private static string ReadText(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd().TrimEnd('\n', '\r');
    }

    private static int LongestBacktickRun(string text)
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
}
