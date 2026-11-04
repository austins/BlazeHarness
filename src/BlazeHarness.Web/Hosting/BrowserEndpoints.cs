using BlazeHarness.Core.Web;
using BlazeHarness.Core.Web.Browser;
using System.Net.WebSockets;
using System.Threading.Channels;

namespace BlazeHarness.Web.Hosting;

/// <summary>
/// The endpoints of the browser panel: the stream of the pictures of a chat's browser page, so they don't pass through the circuit,
/// and a picture of the whole page.
/// </summary>
public static class BrowserEndpoints
{
    /// <summary>
    /// The route of the stream of one chat's browser pictures.
    /// </summary>
    public const string StreamRoute = "/api/chats/{chatId}/browser/stream";

    /// <summary>
    /// The route of the picture of the whole page a chat's browser has open.
    /// </summary>
    public const string ScreenshotRoute = "/api/chats/{chatId}/browser/screenshot";

    /// <summary>
    /// Gets the file name a saved full-page screenshot is offered under.
    /// </summary>
    public const string ScreenshotFileName = "page-screenshot.png";

    /// <summary>
    /// Get the address of the stream of a chat's browser pictures.
    /// </summary>
    /// <param name="chatId">The chat id.</param>
    /// <returns>The relative address the WebSocket is served at.</returns>
    public static string StreamAddressOf(string chatId)
    {
        return $"api/chats/{Uri.EscapeDataString(chatId)}/browser/stream";
    }

    /// <summary>
    /// Get the address of the picture of the whole page a chat's browser has open.
    /// </summary>
    /// <param name="chatId">The chat id.</param>
    /// <returns>The relative address the PNG is served at.</returns>
    public static string ScreenshotAddressOf(string chatId)
    {
        return $"api/chats/{Uri.EscapeDataString(chatId)}/browser/screenshot";
    }

    /// <summary>
    /// Add the browser panel endpoints.
    /// </summary>
    /// <param name="app">The app to add the endpoint to.</param>
    /// <returns>The same app, to chain calls.</returns>
    public static WebApplication MapBrowser(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGet(StreamRoute, StreamAsync);
        app.MapGet(ScreenshotRoute, ScreenshotAsync);
        return app;
    }

    /// <summary>
    /// Send the pictures of a chat's browser page over a WebSocket, each one as a binary JPEG message.
    /// </summary>
    /// <remarks>
    /// The latest picture is sent first and then each new one. A slow client skips the pictures it missed instead of
    /// queueing them, so it always catches up to the page as it is now.
    /// </remarks>
    /// <param name="chatId">The chat id.</param>
    /// <param name="web">The web services, which own the browser.</param>
    /// <param name="context">The current request.</param>
    /// <returns>A task that completes when the client has gone.</returns>
    public static async Task StreamAsync(string chatId, WebServices web, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(web);
        ArgumentNullException.ThrowIfNull(context);

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var changed = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        void OnChanged(object? sender, BrowserFrameChangedEventArgs e)
        {
            if (e.ChatId == chatId && e.Frame is not null)
            {
                changed.Writer.TryWrite(true);
            }
        }

        web.BrowserFrameChanged += OnChanged;
#pragma warning disable CA2025 // The finally block awaits the receiver before the socket and token source are disposed.
        var receiver = ReceiveUntilClosedAsync(socket, stop);
#pragma warning restore CA2025
        try
        {
            changed.Writer.TryWrite(true);
            BrowserFrame? sent = null;
            while (await changed.Reader.WaitToReadAsync(stop.Token))
            {
                changed.Reader.TryRead(out _);
                if (web.LatestBrowserFrame(chatId) is { } frame && !ReferenceEquals(frame, sent))
                {
                    await socket.SendAsync(frame.Jpeg, WebSocketMessageType.Binary, true, stop.Token);
                    sent = frame;
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
            // The client closed the panel or went away; there is nobody left to send to.
        }
        finally
        {
            web.BrowserFrameChanged -= OnChanged;
            await stop.CancelAsync();
            await receiver;
        }
    }

    /// <summary>
    /// Take a PNG of the whole page a chat's browser has open, below the fold included.
    /// </summary>
    /// <param name="chatId">The chat id.</param>
    /// <param name="web">The web services, which own the browser.</param>
    /// <param name="context">The current request.</param>
    /// <returns>The picture, or not found when the chat has no page open or it could not be captured.</returns>
    public static async Task<IResult> ScreenshotAsync(string chatId, WebServices web, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(web);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            var png = await web.BrowserFullPageScreenshotAsync(chatId, context.RequestAborted);
            context.Response.Headers.CacheControl = "no-store";
            return TypedResults.File(png, "image/png", ScreenshotFileName);
        }
        catch (BrowserException)
        {
            return TypedResults.NotFound();
        }
    }

    private static async Task ReceiveUntilClosedAsync(WebSocket socket, CancellationTokenSource stop)
    {
        // The client never sends anything; reading is how a close from its side is noticed.
        var buffer = new byte[16];
        try
        {
            var closed = false;
            while (!closed && socket.State == WebSocketState.Open)
            {
                closed = (await socket.ReceiveAsync(buffer, stop.Token)).MessageType == WebSocketMessageType.Close;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
            // The connection ended; the sender loop stops with it.
        }
        finally
        {
            await stop.CancelAsync();
        }
    }
}
