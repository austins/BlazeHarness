namespace BlazeHarness.Web.Hosting;

/// <summary>
/// Gate in front of everything, including the <c>/_blazor</c> SignalR connection. A request must have an exact
/// loopback <c>Host</c>, a same-origin <c>Origin</c> when one is sent, and the session cookie. The Host check
/// stops DNS rebinding, the Origin check stops other web pages, and the cookie stops other local users.
/// </summary>
/// <param name="next">The next step in the request pipeline.</param>
/// <param name="credentials">The per-launch secrets that decide who may connect.</param>
public sealed class LocalAccessMiddleware(RequestDelegate next, LaunchCredentials credentials)
{
    /// <summary>
    /// The path that trades the launch token for a session cookie.
    /// </summary>
    public const string LaunchPath = "/launch";

    /// <summary>
    /// Allow or reject a request.
    /// </summary>
    /// <param name="context">The current request.</param>
    /// <returns>A task that completes when the request was rejected, redirected, or passed on.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var port = context.Connection.LocalPort;
        var allowedHosts = new[] { $"127.0.0.1:{port}", $"localhost:{port}" };

        if (!allowedHosts.Contains(context.Request.Host.Value, StringComparer.OrdinalIgnoreCase))
        {
            await RejectAsync(context, StatusCodes.Status403Forbidden, "Unexpected Host header.");
            return;
        }

        var origin = context.Request.Headers.Origin.ToString();
        var isSignalR = context.Request.Path.StartsWithSegments("/_blazor", StringComparison.OrdinalIgnoreCase);
        if (origin.Length > 0)
        {
            if (!allowedHosts.Any(h => string.Equals(origin, "http://" + h, StringComparison.OrdinalIgnoreCase)))
            {
                await RejectAsync(context, StatusCodes.Status403Forbidden, "Unexpected Origin header.");
                return;
            }
        }
        else if (isSignalR)
        {
            await RejectAsync(context, StatusCodes.Status403Forbidden, "Missing Origin header.");
            return;
        }

        context.Response.Headers["Referrer-Policy"] = "no-referrer";

        if (context.Request.Path.Equals(LaunchPath, StringComparison.OrdinalIgnoreCase) && HttpMethods.IsGet(context.Request.Method))
        {
            if (!credentials.TryConsumeToken(context.Request.Query["token"]))
            {
                await RejectAsync(
                    context,
                    StatusCodes.Status401Unauthorized,
                    "This launch link is invalid or was already used. Restart BlazeHarness to get a new one.");
                return;
            }

            // Not Secure: the app is plain HTTP on loopback, where browsers differ on Secure-cookie handling.
#pragma warning disable S2092
            context.Response.Cookies.Append(
                LaunchCredentials.CookieName,
                credentials.SessionSecret,
                new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/", IsEssential = true });
#pragma warning restore S2092
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Redirect("/");
            return;
        }

        if (!credentials.IsValidSession(context.Request.Cookies[LaunchCredentials.CookieName]))
        {
            await RejectAsync(context, StatusCodes.Status401Unauthorized, "Open BlazeHarness with the link it printed at startup.");
            return;
        }

        await next(context);
    }

    private static Task RejectAsync(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/plain; charset=utf-8";
        return context.Response.WriteAsync(message, context.RequestAborted);
    }
}
