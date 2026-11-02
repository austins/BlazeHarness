using BlazeHarness.Core.Config;
using System.Text;
using System.Text.Json;

namespace BlazeHarness.Core.Chats;

/// <summary>
/// Turns a chat into a file.
/// </summary>
internal static class ChatExport
{
    /// <summary>
    /// The value of the <c>format</c> property of an exported JSON file.
    /// </summary>
    public const string JsonFormat = "blazeharness-chat";

    /// <summary>
    /// The version of the JSON layout that this code writes and reads.
    /// </summary>
    public const int JsonVersion = 1;

    private const int MaxFileNameLength = 50;

    /// <summary>
    /// Export a chat as a file.
    /// </summary>
    /// <remarks>
    /// Attached files are not included, only their names in the Markdown transcript.
    /// The text may hold secrets that a tool printed, such as the contents of a file it read.
    /// </remarks>
    /// <param name="info">The chat's metadata.</param>
    /// <param name="events">The chat's event log, with every tool result whole.</param>
    /// <param name="format">The file format.</param>
    /// <param name="exportedAt">The time to write in the file.</param>
    /// <returns>The file.</returns>
    public static ExportedFile Export(ChatInfo info, IReadOnlyList<ChatEvent> events, ExportFormat format, DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(events);

        var name = FileNameOf(info);
        return format == ExportFormat.Json
            ? new ExportedFile(name + ".json", "application/json", ToJson(info, events, exportedAt))
            : new ExportedFile(name + ".md", "text/markdown", ChatMarkdown.Write(info, events));
    }

    /// <summary>
    /// Write the whole event log of a chat as JSON, which <see cref="ChatImport"/> can read back.
    /// </summary>
    /// <param name="info">The chat's metadata.</param>
    /// <param name="events">The chat's event log, with every tool result whole.</param>
    /// <param name="exportedAt">The time to write in the file.</param>
    /// <returns>The JSON text.</returns>
    public static string ToJson(ChatInfo info, IReadOnlyList<ChatEvent> events, DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(events);

        var document = new ChatDocument(
            JsonFormat,
            JsonVersion,
            exportedAt,
            new ChatDocumentInfo(info.Title, info.TitleSource, info.CreatedAt, info.UpdatedAt, info.ProviderId, info.ModelId, info.Level),
            events);
        return JsonSerializer.Serialize(document, JsonDefaults.Options);
    }

    // A name that is safe on every file system: lowercase letters, digits and dashes.
    private static string FileNameOf(ChatInfo info)
    {
        var name = new StringBuilder();
        foreach (var c in info.Title)
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                name.Append(c);
            }
            else if (c is >= 'A' and <= 'Z')
            {
                name.Append((char)(c + ('a' - 'A')));
            }
            else if (name.Length > 0 && name[^1] != '-')
            {
                name.Append('-');
            }

            if (name.Length >= MaxFileNameLength)
            {
                break;
            }
        }

        var text = name.ToString().Trim('-');
        return text.Length == 0 ? "chat" : text;
    }
}
