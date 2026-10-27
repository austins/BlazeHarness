using BlazeHarness.Core.Sessions;

namespace BlazeHarness.Web.Hosting;

/// <summary>
/// The endpoint that downloads a chat as a file.
/// </summary>
public static class ExportEndpoints
{
    /// <summary>
    /// The route of a chat export.
    /// </summary>
    public const string Route = "/api/sessions/{sessionId}/export";

    private const string JsonFormat = "json";
    private const string MarkdownFormat = "md";

    /// <summary>
    /// Get the address of a chat export.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="format">The file format.</param>
    /// <returns>The relative address the file is served at.</returns>
    public static string AddressOf(string sessionId, ExportFormat format)
    {
        return $"api/sessions/{Uri.EscapeDataString(sessionId)}/export?format={(format == ExportFormat.Json ? JsonFormat : MarkdownFormat)}";
    }

    /// <summary>
    /// Add the chat export endpoint.
    /// </summary>
    /// <param name="app">The app to add the endpoint to.</param>
    /// <returns>The same app, to chain calls.</returns>
    public static WebApplication MapExport(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGet(Route, ServeAsync);
        return app;
    }

    /// <summary>
    /// Serve a chat as a download.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="format">"json" for the importable file; anything else gives the Markdown transcript.</param>
    /// <param name="sessions">The chat sessions.</param>
    /// <param name="context">The current request.</param>
    /// <param name="cancellationToken">A token that is cancelled when the request is aborted.</param>
    /// <returns>The file as an attachment, or a not found response.</returns>
    public static async Task<IResult> ServeAsync(string sessionId, string? format, SessionService sessions, HttpContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(context);

        var file = await sessions.ExportAsync(sessionId, format == JsonFormat ? ExportFormat.Json : ExportFormat.Markdown, cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            return Results.NotFound();
        }

        context.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.File(System.Text.Encoding.UTF8.GetBytes(file.Content), file.ContentType + "; charset=utf-8", file.FileName);
    }
}
