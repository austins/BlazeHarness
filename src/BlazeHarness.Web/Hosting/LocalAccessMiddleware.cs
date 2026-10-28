using System.Net;

namespace BlazeHarness.Web.Hosting;

/// <summary>
/// Gate in front of everything, including the <c>/_blazor</c> SignalR connection. A request must have a <c>Host</c> that is
/// <c>localhost</c>, an IP address, or (when listening on all interfaces) this machine's name on the listening port, and a same-origin <c>Origin</c> when one is sent (always for SignalR
/// connections). The Host check stops DNS rebinding, which needs a domain name, and the Origin check stops other web pages.
/// There is no sign-in: whoever can reach the port can use the app, so the listening address is the access control.
/// </summary>
/// <param name="next">The next step in the request pipeline.</param>
/// <param name="policy">Whether IP addresses other than loopback are accepted in the <c>Host</c> header.</param>
public sealed class LocalAccessMiddleware(RequestDelegate next, AccessPolicy policy)
{
    /// <summary>
    /// The policy for app pages: own scripts only, own origin for the circuit's WebSocket, no framing.
    /// Inline styles stay allowed because Blazor and the markdown output set style attributes.
    /// </summary>
    public const string PageContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; "
        + "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    private static readonly string[] PrivateSuffixes = [".lan", ".local", ".localdomain", ".home", ".internal", ".home.arpa"];

    /// <summary>
    /// Allow or reject a request.
    /// </summary>
    /// <param name="context">The current request.</param>
    /// <returns>A task that completes when the request was rejected or passed on.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var host = context.Request.Host;
        if (!IsAllowedHost(host, context.Connection.LocalPort))
        {
            await RejectAsync(context, StatusCodes.Status403Forbidden, "Unexpected Host header.");
            return;
        }

        var expectedOrigin = "http://" + host.Value;
        var origin = context.Request.Headers.Origin.ToString();
        var isSignalRConnection = context.Request.Path.StartsWithSegments("/_blazor", StringComparison.OrdinalIgnoreCase)
            && (!HttpMethods.IsGet(context.Request.Method) || context.WebSockets.IsWebSocketRequest);
        if (origin.Length > 0)
        {
            if (!string.Equals(origin, expectedOrigin, StringComparison.OrdinalIgnoreCase))
            {
                await RejectAsync(context, StatusCodes.Status403Forbidden, "Unexpected Origin header.");
                return;
            }
        }
        else if (isSignalRConnection)
        {
            await RejectAsync(context, StatusCodes.Status403Forbidden, "Missing Origin header.");
            return;
        }

        var headers = context.Response.Headers;
        headers["Referrer-Policy"] = "no-referrer";
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
#pragma warning disable S7039 // style-src needs unsafe-inline; scripts stay locked to self.
        headers.ContentSecurityPolicy = PageContentSecurityPolicy;
#pragma warning restore S7039

        await next(context);
    }

    private bool IsAllowedHost(HostString host, int port)
    {
        if (host.Port != port)
        {
            return false;
        }

        var name = host.Host.Trim('[', ']');
        if (name is "127.0.0.1" || name.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return policy.AllowAnyHost && (IPAddress.TryParse(name, out _) || IsMachineName(name));
    }

    private bool IsMachineName(string name)
    {
        if (policy.MachineName.Length == 0)
        {
            return false;
        }

        if (name.Equals(policy.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = policy.MachineName + ".";
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && PrivateSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static Task RejectAsync(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/plain; charset=utf-8";
        return context.Response.WriteAsync(message, context.RequestAborted);
    }
}
