namespace BlazeHarness.Core.Sessions;

/// <summary>
/// The file formats a chat can be exported as.
/// </summary>
public enum ExportFormat
{
    /// <summary>A readable transcript in Markdown, with each tool call collapsed.</summary>
    Markdown,

    /// <summary>The whole event log as JSON, which can be imported again.</summary>
    Json,
}
