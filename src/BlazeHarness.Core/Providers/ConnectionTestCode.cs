namespace BlazeHarness.Core.Providers;

/// <summary>
/// Which outcome a <see cref="ConnectionTestResult"/> reports, so a UI can show it in the user's language.
/// </summary>
public enum ConnectionTestCode
{
    /// <summary>The endpoint listed models; the <c>count</c> argument holds how many.</summary>
    Connected,

    /// <summary>The endpoint answered but listed no models.</summary>
    ConnectedNoModels,

    /// <summary>The base URL is not a valid http or https address.</summary>
    InvalidBaseUrl,

    /// <summary>The model list is too large to be an OpenAI-style answer.</summary>
    ModelListTooLarge,

    /// <summary>The server did not answer in time; the <c>seconds</c> argument holds the limit.</summary>
    Timeout,

    /// <summary>The connection failed; the <c>detail</c> argument holds the system's explanation.</summary>
    ConnectionFailed,

    /// <summary>The answer was not an OpenAI-style model list.</summary>
    NotAModelList,

    /// <summary>The server rejected the API key.</summary>
    Unauthorized,

    /// <summary>The server has no models endpoint at this address.</summary>
    NotFound,

    /// <summary>The server is rate limiting requests.</summary>
    RateLimited,

    /// <summary>The server answered with another HTTP status; the <c>code</c> and <c>status</c> arguments describe it.</summary>
    HttpError,
}
