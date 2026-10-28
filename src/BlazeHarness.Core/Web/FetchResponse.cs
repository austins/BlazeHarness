namespace BlazeHarness.Core.Web;

/// <summary>
/// What a fetch got back.
/// </summary>
/// <param name="FinalUri">The address the body came from, after redirects on the same site.</param>
/// <param name="Status">The HTTP status code.</param>
/// <param name="ContentType">The media type without parameters, or null when the server gave none.</param>
/// <param name="Charset">The charset parameter of the content type, or null.</param>
/// <param name="Body">The body, cut at the size limit.</param>
/// <param name="Truncated">Whether the body was longer than the limit.</param>
/// <param name="RedirectTo">The address a redirect to another site points at; the body is empty then.</param>
/// <param name="BotChallenge">Whether the response looks like a bot check page rather than the page asked for.</param>
internal sealed record FetchResponse(
    Uri FinalUri,
    int Status,
    string? ContentType,
    string? Charset,
    byte[] Body,
    bool Truncated,
    Uri? RedirectTo = null,
    bool BotChallenge = false);
