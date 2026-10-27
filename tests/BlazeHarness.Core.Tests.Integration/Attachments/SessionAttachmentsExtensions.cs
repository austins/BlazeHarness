using BlazeHarness.Core.Attachments;
using System.Text;

namespace BlazeHarness.Core.Tests.Integration.Attachments;

/// <summary>Shortcuts for saving in-memory content as an attachment.</summary>
public static class SessionAttachmentsExtensions
{
    /// <summary>Save UTF-8 text as an attachment.</summary>
    /// <param name="attachments">The attachments to save into.</param>
    /// <param name="name">The file name to save it under.</param>
    /// <param name="content">The text to save.</param>
    /// <returns>The saved attachment.</returns>
    public static Task<AttachmentInfo> SaveAsync(this SessionAttachments attachments, string name, string content)
        => attachments.SaveAsync(name, Encoding.UTF8.GetBytes(content));

    /// <summary>Save bytes as an attachment.</summary>
    /// <param name="attachments">The attachments to save into.</param>
    /// <param name="name">The file name to save it under.</param>
    /// <param name="content">The bytes to save.</param>
    /// <returns>The saved attachment.</returns>
    public static async Task<AttachmentInfo> SaveAsync(this SessionAttachments attachments, string name, byte[] content)
    {
        await using var stream = new MemoryStream(content);
        return await attachments.SaveAsync(name, stream, TestContext.Current.CancellationToken);
    }
}
