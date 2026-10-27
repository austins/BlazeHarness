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
