namespace BlazeHarness.Core.Web.Browser;

/// <summary>
/// The browser could not do what the model asked, with a message that tells the model why and holds nothing the page sent.
/// </summary>
public sealed class BrowserException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserException"/> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    public BrowserException(string message)
        : base(message)
    {
    }
}
