namespace BlazeHarness.Core.Chats.Export;

/// <summary>
/// A file is not a chat export this version can read. The message is in English; <see cref="Error"/> lets a UI say it
/// in the user's language.
/// </summary>
public sealed class ChatImportException : FormatException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChatImportException"/> class.
    /// </summary>
    public ChatImportException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatImportException"/> class.
    /// </summary>
    /// <param name="message">Why the file can't be imported.</param>
    public ChatImportException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatImportException"/> class.
    /// </summary>
    /// <param name="message">Why the file can't be imported.</param>
    /// <param name="innerException">The error that caused this one.</param>
    public ChatImportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatImportException"/> class.
    /// </summary>
    /// <param name="error">Why the file can't be imported.</param>
    public ChatImportException(ChatImportError error)
        : base(error switch
        {
            ChatImportError.NewerVersion => "The file was exported by a newer version of BlazeHarness.",
            ChatImportError.NoMessages => "The file has no messages.",
            _ => "The file is not a BlazeHarness chat export.",
        })
    {
        Error = error;
    }

    /// <summary>
    /// Gets why the file can't be imported, or null when only the message says it.
    /// </summary>
    public ChatImportError? Error { get; }
}
