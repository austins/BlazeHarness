using BlazeHarness.Core.Attachments;
using Microsoft.AspNetCore.Components.Forms;
using System.Runtime.InteropServices;

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
    /// The start of the name that the composer gives a long pasted text; <c>chat.js</c> uses the same prefix.
    /// </summary>
    public const string PastedNamePrefix = "pasted-text-";

    /// <summary>
    /// Gets a value indicating whether the file is a long text the user pasted, which can be previewed and put back as text.
    /// </summary>
    public bool IsPasted => Name.StartsWith(PastedNamePrefix, StringComparison.Ordinal);

    /// <summary>
    /// Gets a value indicating whether the browser reported the file as an image.
    /// </summary>
    public bool IsImage => ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Open the content for reading, without copying it when it is backed by an array.
    /// </summary>
    /// <returns>A read-only stream over the content.</returns>
    public Stream OpenRead()
    {
        return MemoryMarshal.TryGetArray(Content, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(Content.ToArray(), writable: false);
    }

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
