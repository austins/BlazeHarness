namespace BlazeHarness.Core.Mcp;

/// <summary>
/// The error when a server could not be started or reached. The message is fit to show the user.
/// </summary>
public sealed class McpConnectException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpConnectException"/> class.
    /// </summary>
    public McpConnectException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpConnectException"/> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    public McpConnectException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpConnectException"/> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The underlying error.</param>
    public McpConnectException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
