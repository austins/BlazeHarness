namespace BlazeHarness.Core.Web.Browser;

/// <summary>
/// One picture of the page a chat's browser shows, for the view next to the chat.
/// </summary>
/// <param name="Jpeg">The picture as a JPEG.</param>
/// <param name="Url">The address of the page.</param>
/// <param name="Title">The page title, which may be empty.</param>
public sealed record BrowserFrame(ReadOnlyMemory<byte> Jpeg, Uri Url, string Title);
