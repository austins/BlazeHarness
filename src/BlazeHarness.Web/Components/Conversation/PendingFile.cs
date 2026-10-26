using BlazeHarness.Core.Attachments;
using Microsoft.AspNetCore.Components.Forms;

namespace BlazeHarness.Web.Components.Conversation;

/// <summary>
/// A file the user picked, dropped or pasted that waits in the composer until the message is sent.
/// </summary>
/// <param name="Id">An identifier that tells the files in the composer apart, as names can repeat.</param>
/// <param name="Name">The file name the browser gave.</param>
/// <param name="ContentType">The type the browser reported, which is only used to warn about images.</param>
/// <param name="Content">The file content.</param>
public sealed record PendingFile(Guid Id, string Name, string ContentType, ReadOnlyMemory<byte> Content)
{
    /// <summary>
    /// Gets a value indicating whether the browser reported the file as an image.
    /// </summary>
    public bool IsImage => ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Read a file the browser handed over.
    /// </summary>
    /// <param name="file">The picked file, which may not be larger than <see cref="SessionAttachments.MaxFileBytes"/>.</param>
    /// <returns>The file with its content in memory.</returns>
    public static async Task<PendingFile> ReadAsync(IBrowserFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

#pragma warning disable S5693 // The size is capped by MaxFileBytes, which is above the default limit on purpose.
        await using var stream = file.OpenReadStream(SessionAttachments.MaxFileBytes);
#pragma warning restore S5693
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return new PendingFile(Guid.NewGuid(), file.Name, file.ContentType, buffer.ToArray());
    }
}
