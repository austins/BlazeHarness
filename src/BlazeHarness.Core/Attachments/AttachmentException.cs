namespace BlazeHarness.Core.Attachments;

/// <summary>
/// A file can't be attached. The message is written for the user.
/// </summary>
public sealed class AttachmentException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AttachmentException"/> class.
    /// </summary>
    public AttachmentException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AttachmentException"/> class.
    /// </summary>
    /// <param name="message">Why the file can't be attached.</param>
    public AttachmentException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AttachmentException"/> class.
    /// </summary>
    /// <param name="message">Why the file can't be attached.</param>
    /// <param name="innerException">The error that caused this one.</param>
    public AttachmentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
