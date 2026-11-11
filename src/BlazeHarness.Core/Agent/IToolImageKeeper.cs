using BlazeHarness.Core.Attachments;
using Microsoft.Extensions.AI;

namespace BlazeHarness.Core.Agent;

/// <summary>
/// Keeps a copy of the pictures tool calls produce, so the model can be shown them again, and read them by attachment ID.
/// </summary>
internal interface IToolImageKeeper
{
    /// <summary>
    /// Save a copy of a picture in the chat's attachments.
    /// </summary>
    /// <param name="toolName">The tool that produced the picture.</param>
    /// <param name="image">The picture.</param>
    /// <returns>The saved copy, or null when it could not be kept.</returns>
    public Task<AttachmentInfo?> KeepAsync(string toolName, DataContent image);
}
