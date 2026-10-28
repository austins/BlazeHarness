using System.Net;
using System.Net.Sockets;

namespace BlazeHarness.Core.Web;

/// <summary>
/// Downloads web pages for the fetch tool, and only from public internet addresses.
/// </summary>
/// <remarks>
/// <para>
/// The address check happens when the connection is made, on the address that is then used, so a name that resolves to a private address
/// (or changes its answer between a check and the connection) can't reach this computer or the local network. Redirects are followed by hand:
/// a redirect on the same site is followed, and one to another site is handed back to the caller, because the user's permission is per site.
/// </para>
/// <para>
/// Nothing is sent that identifies the user: no cookies, no <c>Referer</c>, and no proxy settings are used.
/// </para>
/// </remarks>
internal sealed class SafeWebClient : IDisposable
{
    internal const int MaxPageBytes = 10 * 1024 * 1024;
    internal const int MaxPdfBytes = 32 * 1024 * 1024;

    private const int MaxRedirects = 5;
    private const int MaxErrorBodyBytes = 64 * 1024;
    private const string UserAgent = "Mozilla/5.0 (compatible; BlazeHarness/1.0; +https://github.com/austins/BlazeHarness)";

    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="SafeWebClient"/> class.
    /// </summary>
    /// <param name="isAllowed">Tells whether an address may be connected to; public internet addresses only when null.</param>
    /// <param name="handler">The handler to send with, which tests use to avoid the network; a handler that applies the address check when null.</param>
    public SafeWebClient(Func<IPAddress, bool>? isAllowed = null, HttpMessageHandler? handler = null)
    {
#pragma warning disable CA2000 // The client owns the handler and disposes it.
        _client = new HttpClient(handler ?? CreateHandler(isAllowed ?? WebAddressPolicy.IsPublic)) { Timeout = Timeout.InfiniteTimeSpan };
#pragma warning restore CA2000
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();

    /// <summary>
    /// Download an address.
    /// </summary>
    /// <param name="uri">An address accepted by <see cref="WebUrl.TryParse"/>.</param>
    /// <param name="cancellationToken">A token that stops the download.</param>
    /// <returns>The response; a status outside 200-299 is returned, not thrown.</returns>
    /// <exception cref="WebFetchException">The site could not be reached or the response can't be used.</exception>
    public async Task<FetchResponse> FetchAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var current = uri;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/pdf;q=0.9,text/plain;q=0.8,*/*;q=0.5");
            request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.8");
            using var response = await SendAsync(request, cancellationToken);
            if (IsRedirect(response.StatusCode))
            {
                var target = RedirectTarget(current, response);
                if (!WebUrl.SameSite(current, target))
                {
                    return new FetchResponse(current, (int)response.StatusCode, null, null, [], false, RedirectTo: target);
                }

                current = target;
                continue;
            }

            return await ReadAsync(current, response, cancellationToken);
        }

        throw new WebFetchException("The site redirected too many times.");
    }

    private static SocketsHttpHandler CreateHandler(Func<IPAddress, bool> isAllowed)
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxResponseHeadersLength = 64,
            ConnectCallback = (context, token) => ConnectAsync(context, isAllowed, token),
        };
    }

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, Func<IPAddress, bool> isAllowed, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host.Trim('[', ']');
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, cancellationToken);
        var allowed = addresses.Where(isAllowed).ToList();
        if (allowed.Count == 0)
        {
            throw new WebFetchException("The address is not on the public internet, so it was not fetched.");
        }

        SocketException? last = null;
        foreach (var address in allowed)
        {
#pragma warning disable CA2000, IDISP001 // Disposed on every failure path, and owned by the returned stream on success.
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
#pragma warning restore CA2000, IDISP001
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                last = ex;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw last ?? new SocketException((int)SocketError.HostUnreachable);
    }

    private static bool IsRedirect(HttpStatusCode status) => status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static Uri RedirectTarget(Uri current, HttpResponseMessage response)
    {
        if (response.Headers.Location is not { } location)
        {
            throw new WebFetchException("The site redirected without saying where to.");
        }

        var target = location.IsAbsoluteUri ? location : new Uri(current, location);
        return WebUrl.TryParse(target.AbsoluteUri, out var accepted, out _)
            ? accepted
            : throw new WebFetchException("The site redirected to an address that can't be fetched.");
    }

    private static bool LooksLikeChallenge(HttpResponseMessage response, ReadOnlySpan<byte> body)
    {
        if (response.Headers.TryGetValues("cf-mitigated", out var mitigated) && mitigated.Any(value => value.Contains("challenge", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if ((int)response.StatusCode is not (403 or 429 or 503))
        {
            return false;
        }

        var head = System.Text.Encoding.UTF8.GetString(body[..Math.Min(body.Length, 8192)]);
        return head.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)
            || head.Contains("cf-challenge", StringComparison.OrdinalIgnoreCase)
            || head.Contains("captcha", StringComparison.OrdinalIgnoreCase)
            || head.Contains("Attention Required", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.InnerException is WebFetchException blocked)
        {
            throw new WebFetchException(blocked.Message);
        }
        catch (HttpRequestException ex)
        {
            throw new WebFetchException($"The site could not be reached ({ex.HttpRequestError}).");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WebFetchException("The site took too long to answer.");
        }
    }

    private static async Task<FetchResponse> ReadAsync(Uri address, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var type = response.Content.Headers.ContentType;
        var media = type?.MediaType;
        var success = response.IsSuccessStatusCode;
        var limit = success ? (string.Equals(media, "application/pdf", StringComparison.OrdinalIgnoreCase) ? MaxPdfBytes : MaxPageBytes) : MaxErrorBodyBytes;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[32 * 1024];
        var truncated = false;
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                var room = limit - (int)memory.Length;
                if (read > room)
                {
                    await memory.WriteAsync(buffer.AsMemory(0, room), cancellationToken);
                    truncated = true;
                    break;
                }

                await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            throw new WebFetchException("The connection broke while the page was being read.");
        }

        var body = memory.ToArray();
        return new FetchResponse(
            address,
            (int)response.StatusCode,
            type?.MediaType,
            type?.CharSet?.Trim('"'),
            body,
            truncated,
            BotChallenge: LooksLikeChallenge(response, body));
    }
}
