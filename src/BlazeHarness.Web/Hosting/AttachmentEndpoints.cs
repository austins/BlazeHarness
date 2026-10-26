using BlazeHarness.Core.Sessions;

namespace BlazeHarness.Web.Hosting;

/// <summary>
/// The endpoint that serves the files attached to a chat, so the chat can show images and link to text files.
/// </summary>
public static class AttachmentEndpoints
{
    /// <summary>
    /// The route of one attachment.
    /// </summary>
    public const string Route = "/api/sessions/{sessionId}/attachments/{attachmentId}";

    /// <summary>
    /// Get the address of an attachment.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="attachmentId">The attachment id.</param>
    /// <returns>The relative address the file is served at.</returns>
    public static string AddressOf(string sessionId, string attachmentId)
    {
        return $"api/sessions/{Uri.EscapeDataString(sessionId)}/attachments/{Uri.EscapeDataString(attachmentId)}";
    }

    /// <summary>
    /// Add the attachment endpoint.
    /// </summary>
    /// <param name="app">The app to add the endpoint to.</param>
    /// <returns>The same app, to chain calls.</returns>
    public static WebApplication MapAttachments(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGet(Route, Serve);
        return app;
    }

    /// <summary>
    /// Serve one attached file.
    /// </summary>
    /// <remarks>
    /// Only the content type decided when the file was saved is sent, and the response may not be sniffed or run as a page,
    /// so a text file that looks like HTML is shown as plain text.
    /// </remarks>
    /// <param name="sessionId">The session id.</param>
    /// <param name="attachmentId">The attachment id.</param>
    /// <param name="sessions">The chat sessions.</param>
    /// <param name="context">The current request.</param>
    /// <returns>The file, or a not found response.</returns>
    public static IResult Serve(string sessionId, string attachmentId, SessionService sessions, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(context);

        var file = sessions.FindAttachment(sessionId, attachmentId);
        if (file is null)
        {
            return Results.NotFound();
        }

        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'; img-src 'self'";
        var type = file.MediaType == "text/plain" ? "text/plain; charset=utf-8" : file.MediaType;
        return Results.File(file.Path, type);
    }
}
