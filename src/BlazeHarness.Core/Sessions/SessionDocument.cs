using BlazeHarness.Core.Providers;

namespace BlazeHarness.Core.Sessions;

/// <summary>
/// The JSON file of an exported chat.
/// </summary>
/// <remarks>
/// It holds what is needed to read the chat again, and leaves out what only makes sense on the machine it came from:
/// the folder, the permissions, the MCP and skill choices and the attached files.
/// </remarks>
/// <param name="Format">Always <see cref="SessionExport.JsonFormat"/>, so other JSON files are not mistaken for a chat.</param>
/// <param name="Version">The version of this layout.</param>
/// <param name="ExportedAt">When the file was made.</param>
/// <param name="Session">The chat's title, times and last model.</param>
/// <param name="Events">The chat's event log, with every tool result whole.</param>
internal sealed record SessionDocument(string Format, int Version, DateTimeOffset ExportedAt, SessionDocumentInfo Session, IReadOnlyList<SessionEvent> Events);

/// <summary>
/// The part of a chat's metadata that is exported.
/// </summary>
/// <param name="Title">The title.</param>
/// <param name="TitleSource">Where the title came from, so a title the user typed is not replaced.</param>
/// <param name="CreatedAt">When the chat was created.</param>
/// <param name="UpdatedAt">When the chat last had activity.</param>
/// <param name="ProviderId">The id of the provider the chat last used.</param>
/// <param name="ModelId">The id of the model the chat last used.</param>
/// <param name="Level">The thinking level the chat last used.</param>
internal sealed record SessionDocumentInfo(
    string Title,
    TitleSource TitleSource,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? ProviderId,
    string? ModelId,
    ThinkingLevel? Level);
