using BlazeHarness.Core.Chats;

namespace BlazeHarness.Web.Hosting;

/// <summary>
/// The endpoint that downloads a chat as a file.
/// </summary>
public static class ExportEndpoints
{
    /// <summary>
    /// The route of a chat export.
    /// </summary>
    public const string Route = "/api/chats/{chatId}/export";

    private const string JsonFormat = "json";
    private const string MarkdownFormat = "md";

    /// <summary>
    /// Get the address of a chat export.
    /// </summary>
    /// <param name="chatId">The chat id.</param>
    /// <param name="format">The file format.</param>
    /// <returns>The relative address the file is served at.</returns>
    public static string AddressOf(string chatId, ExportFormat format)
    {
        return $"api/chats/{Uri.EscapeDataString(chatId)}/export?format={(format == ExportFormat.Json ? JsonFormat : MarkdownFormat)}";
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
    /// <param name="chatId">The chat id.</param>
    /// <param name="format">"json" for the importable file; anything else gives the Markdown transcript.</param>
    /// <param name="chatService">The chat service.</param>
    /// <param name="context">The current request.</param>
    /// <param name="cancellationToken">A token that is cancelled when the request is aborted.</param>
    /// <returns>The file as an attachment, or a not found response.</returns>
    public static async Task<IResult> ServeAsync(string chatId, string? format, ChatService chatService, HttpContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chatService);
        ArgumentNullException.ThrowIfNull(context);

        var file = await chatService.ExportAsync(chatId, format == JsonFormat ? ExportFormat.Json : ExportFormat.Markdown, cancellationToken);
        if (file is null)
        {
            return Results.NotFound();
        }

        context.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.File(System.Text.Encoding.UTF8.GetBytes(file.Content), file.ContentType + "; charset=utf-8", file.FileName);
    }
}
