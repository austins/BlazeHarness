using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace BlazeHarness.Core.Web.Search.Providers;

/// <summary>
/// Sends a provider's request and turns every way it can fail into a <see cref="SearchProviderException"/>.
/// </summary>
internal static class SearchHttp
{
    private const int MaxResponseBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Post a JSON body and read the response body as text, within a size limit.
    /// </summary>
    /// <param name="client">The client to send with.</param>
    /// <param name="endpoint">The address to post to.</param>
    /// <param name="json">The request body.</param>
    /// <param name="acceptEventStream">Whether the reply may also be a server-sent event stream.</param>
    /// <param name="cancellationToken">A token that stops the request.</param>
    /// <returns>The body text of a successful response.</returns>
    /// <exception cref="SearchProviderException">The status was not a success, or the request failed.</exception>
    public static async Task<string> PostJsonAsync(HttpClient client, Uri endpoint, string json, bool acceptEventStream, CancellationToken cancellationToken)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Content = content;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (acceptEventStream)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        }

        return await SendAsync(client, request, cancellationToken);
    }

    /// <summary>
    /// Get a page as text, within a size limit.
    /// </summary>
    /// <param name="client">The client to send with.</param>
    /// <param name="address">The address to get.</param>
    /// <param name="userAgent">The <c>User-Agent</c> header value.</param>
    /// <param name="cancellationToken">A token that stops the request.</param>
    /// <returns>The body text of a successful response.</returns>
    /// <exception cref="SearchProviderException">The status was not a success, or the request failed.</exception>
    public static async Task<string> GetAsync(HttpClient client, Uri address, string userAgent, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.UserAgent.ParseAdd(userAgent);
        return await SendAsync(client, request, cancellationToken);
    }

    private static async Task<string> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            ThrowForStatus(response);
            return await ReadBodyAsync(response, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new SearchProviderException(SearchFailure.Unavailable);
        }
        catch (IOException)
        {
            throw new SearchProviderException(SearchFailure.Unavailable);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SearchProviderException(SearchFailure.Unavailable);
        }
    }

    private static void ThrowForStatus(HttpResponseMessage response)
    {
        var code = (int)response.StatusCode;
        if (code is >= 200 and < 300 && response.StatusCode != HttpStatusCode.Accepted)
        {
            return;
        }

        // DuckDuckGo answers 202 with no results when it wants the caller to back off.
        throw response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests or HttpStatusCode.Accepted => new SearchProviderException(SearchFailure.RateLimited, response.Headers.RetryAfter?.Delta),
            HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.PaymentRequired => new SearchProviderException(SearchFailure.Blocked),
            _ when code >= 500 => new SearchProviderException(SearchFailure.Unavailable),
            _ => new SearchProviderException(SearchFailure.BadResponse),
        };
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaxResponseBytes)
        {
            throw new SearchProviderException(SearchFailure.BadResponse);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (memory.Length + read > MaxResponseBytes)
            {
                throw new SearchProviderException(SearchFailure.BadResponse);
            }

            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return ReadEncoding(response.Content.Headers.ContentType?.CharSet).GetString(memory.GetBuffer(), 0, (int)memory.Length);
    }

    private static Encoding ReadEncoding(string? charset)
    {
        if (string.IsNullOrEmpty(charset))
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(charset.Trim('"'));
        }
        catch (ArgumentException)
        {
            // An unknown charset name: UTF-8 is the best guess.
            return Encoding.UTF8;
        }
    }
}
