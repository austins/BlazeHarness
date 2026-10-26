namespace BlazeHarness.Core.Attachments;

/// <summary>
/// How an attached file reaches the model.
/// </summary>
public enum AttachmentDelivery
{
    /// <summary>The image is sent as image input.</summary>
    Image,

    /// <summary>The text is added to the message as a fenced block.</summary>
    Inline,

    /// <summary>The message names the file, and the model reads it with <c>read_attachment</c>.</summary>
    OnDemand,
}
