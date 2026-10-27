using BlazeHarness.Core.Config;
using System.Text.Json;

namespace BlazeHarness.Core.Sessions;

/// <summary>
/// Reads a chat from a JSON file written by <see cref="SessionExport"/>.
/// </summary>
/// <remarks>
/// A file may come from anyone, so only the conversation is taken from it. Attached files and saved tool output files
/// are not part of the import, which also means a file cannot make the app read a path it names.
/// </remarks>
public static class SessionImport
{
    private const int MaxTitleLength = 200;

    /// <summary>
    /// Read an exported chat.
    /// </summary>
    /// <param name="json">The text of the file.</param>
    /// <param name="session">The chat when the file is valid; otherwise null.</param>
    /// <param name="error">Why the file can't be imported, in a sentence that can be shown to the user; otherwise an empty string.</param>
    /// <returns>True when the file is a chat export this version can read.</returns>
    public static bool TryParse(string json, out ImportedSession? session, out string error)
    {
        ArgumentNullException.ThrowIfNull(json);
        session = null;

        SessionDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<SessionDocument>(json, JsonDefaults.Options);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            error = "The file is not a BlazeHarness chat export.";
            return false;
        }

        if (document is not { Format: SessionExport.JsonFormat, Session: not null, Events: not null })
        {
            error = "The file is not a BlazeHarness chat export.";
            return false;
        }

        if (document.Version > SessionExport.JsonVersion)
        {
            error = "The file was exported by a newer version of BlazeHarness.";
            return false;
        }

        var events = document.Events.OfType<SessionEvent>().Select(Sanitize).ToList();
        if (!events.Exists(e => e is UserMessageEvent or AssistantStartEvent))
        {
            error = "The file has no messages.";
            return false;
        }

        var title = document.Session.Title ?? string.Empty;
        var cut = title.Length > MaxTitleLength && char.IsHighSurrogate(title[MaxTitleLength - 1]) ? MaxTitleLength - 1 : MaxTitleLength;
        session = new ImportedSession(
            new SessionInfo
            {
                Title = title.Length > MaxTitleLength ? title[..cut] : title,
                TitleSource = document.Session.TitleSource,
                CreatedAt = document.Session.CreatedAt,
                UpdatedAt = document.Session.UpdatedAt,
                ProviderId = document.Session.ProviderId,
                ModelId = document.Session.ModelId,
                Level = document.Session.Level,
            },
            events);
        error = string.Empty;
        return true;
    }

    private static SessionEvent Sanitize(SessionEvent e) => e switch
    {
        UserMessageEvent user => user with { Text = user.Text ?? string.Empty, Attachments = null },
        AssistantDeltaEvent delta => delta with { Text = delta.Text ?? string.Empty, Reasoning = delta.Reasoning ?? string.Empty },
        ToolCallEvent call => call with { CallId = call.CallId ?? string.Empty, Name = call.Name ?? string.Empty, ArgumentsJson = call.ArgumentsJson ?? "{}", Summary = call.Summary ?? string.Empty },
        ToolResultEvent result => result with { CallId = result.CallId ?? string.Empty, Output = result.Output ?? string.Empty, OutputFile = null },
        CompactionEvent compaction => compaction with { Summary = compaction.Summary ?? string.Empty },
        _ => e,
    };
}
