namespace BlazeHarness.Core.Attachments;

/// <summary>
/// A file attached to a message, and how the model received it.
/// </summary>
/// <param name="File">The attached file.</param>
/// <param name="Delivery">How it reached the model, fixed when the message was sent so the history stays the same afterwards.</param>
public sealed record SentAttachment(AttachmentInfo File, AttachmentDelivery Delivery);
