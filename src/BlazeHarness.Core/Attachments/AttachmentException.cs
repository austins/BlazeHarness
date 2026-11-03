namespace BlazeHarness.Core.Attachments;

/// <summary>
/// A file can't be attached. The message is written for the user in English; <see cref="Error"/> lets a UI say it in
/// the user's language.
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

    /// <summary>
    /// Initializes a new instance of the <see cref="AttachmentException"/> class.
    /// </summary>
    /// <param name="error">Why the file can't be attached.</param>
    /// <param name="fileName">The name of the file.</param>
    /// <param name="message">Why the file can't be attached, in English.</param>
    public AttachmentException(AttachmentError error, string fileName, string message)
        : base(message)
    {
        Error = error;
        FileName = fileName;
    }

    /// <summary>
    /// Gets why the file can't be attached, or null when only the message says it.
    /// </summary>
    public AttachmentError? Error { get; }

    /// <summary>
    /// Gets the name of the file, or an empty string when unknown.
    /// </summary>
    public string FileName { get; } = string.Empty;
}
