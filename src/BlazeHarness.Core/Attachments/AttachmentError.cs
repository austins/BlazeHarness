namespace BlazeHarness.Core.Attachments;

/// <summary>
/// Why a file can't be attached, so a UI can say it in the user's language.
/// </summary>
public enum AttachmentError
{
    /// <summary>The file is neither an image the app reads nor text.</summary>
    Unsupported,

    /// <summary>The file is larger than <see cref="ChatAttachments.MaxFileBytes"/>.</summary>
    TooLarge,

    /// <summary>The file is empty.</summary>
    Empty,
}
