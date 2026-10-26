using BlazeHarness.Core.Attachments;
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
    /// The route of an image inside the working folder of a session.
    /// </summary>
    public const string FileRoute = "/api/sessions/{sessionId}/files";

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
    /// Get the address of an image inside the working folder of a session.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="path">The path as the model wrote it.</param>
    /// <returns>The relative address the image is served at.</returns>
    public static string FileAddressOf(string sessionId, string path)
    {
        return $"api/sessions/{Uri.EscapeDataString(sessionId)}/files?path={Uri.EscapeDataString(path)}";
    }

    /// <summary>
    /// Add the attachment and folder image endpoints.
    /// </summary>
    /// <param name="app">The app to add the endpoint to.</param>
    /// <returns>The same app, to chain calls.</returns>
    public static WebApplication MapAttachments(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGet(Route, Serve);
        app.MapGet(FileRoute, ServeFolderImage);
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

        return Send(sessions.FindAttachment(sessionId, attachmentId), context);
    }

    /// <summary>
    /// Serve an image from the working folder of a session.
    /// </summary>
    /// <remarks>
    /// The path is checked against the folder again here, so the endpoint can't be used to read any other file.
    /// Only PNG, JPEG, GIF, WebP, AVIF and SVG files up to the size cap are served.
    /// </remarks>
    /// <param name="sessionId">The session id.</param>
    /// <param name="path">The path of the image, relative to the folder.</param>
    /// <param name="sessions">The chat sessions.</param>
    /// <param name="context">The current request.</param>
    /// <returns>The image, or a not found response.</returns>
    public static IResult ServeFolderImage(string sessionId, string? path, SessionService sessions, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(context);

        return Send(path is null ? null : sessions.FindFolderImage(sessionId, path), context);
    }

    private static IResult Send(AttachmentFile? file, HttpContext context)
    {
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
