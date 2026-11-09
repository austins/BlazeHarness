using BlazeHarness.Core.Attachments;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// A picture a tool call produced.
/// </summary>
/// <param name="Content">The picture.</param>
/// <param name="Attachment">The copy of the picture in the chat's attachments, or null until the chat keeps one.</param>
public sealed record ToolImage(DataContent Content, AttachmentInfo? Attachment = null)
{
    /// <summary>
    /// Gets the size of the picture in bytes.
    /// </summary>
    public long Size => Attachment?.Size ?? Content.Data.Length;
}
