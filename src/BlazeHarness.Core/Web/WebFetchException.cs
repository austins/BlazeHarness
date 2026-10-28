namespace BlazeHarness.Core.Web;

/// <summary>
/// A fetch could not be done, with a message that tells the model why and holds nothing the site sent.
/// </summary>
public sealed class WebFetchException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebFetchException"/> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    public WebFetchException(string message)
        : base(message)
    {
    }
}
