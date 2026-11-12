using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace BlazeHarness.Core.Providers;

/// <summary>
/// Calls a provider's <c>/models</c> endpoint to check the address and key, and to list the models it serves.
/// </summary>
public static class ProviderConnectionTester
{
    /// <summary>
    /// The name of the HTTP client configured for connection checks, with automatic redirects disabled so custom header keys never leave the provider's origin.
    /// </summary>
    public const string HttpClientName = "provider-test";

    private const int MaxBodyBytes = 4 * 1024 * 1024;
    private const int MaxSameOriginRedirects = 3;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Check that an endpoint is reachable and accepts the credentials.
    /// </summary>
    /// <remarks>
    /// Never throws for network or HTTP problems; they become a failed result with a message the user can act on.
    /// The key is only sent to the provider's own base URL.
    /// </remarks>
    /// <param name="provider">The provider to test. Only the base URL and extra headers are used.</param>
    /// <param name="apiKey">The API key, or null for keyless local servers.</param>
    /// <param name="httpClient">The HTTP client to use. The caller owns it.</param>
    /// <param name="cancellationToken">A token to cancel the test.</param>
    /// <returns>The result, including the model ids the endpoint listed.</returns>
    public static Task<ConnectionTestResult> TestAsync(
        ProviderConfig provider,
        string? apiKey,
        HttpClient httpClient,
        CancellationToken cancellationToken = default)
        => TestAsync(provider, apiKey, httpClient, null, cancellationToken);

    /// <summary>
    /// Check that an endpoint is reachable and accepts the credentials.
    /// </summary>
    /// <param name="provider">The provider to test. Only the base URL and extra headers are used.</param>
    /// <param name="apiKey">The API key, or null for keyless local servers.</param>
    /// <param name="httpClient">The HTTP client to use. The caller owns it.</param>
    /// <param name="time">Times the connection check; defaults to the system clock.</param>
    /// <param name="cancellationToken">A token to cancel the test.</param>
    /// <returns>The result, including the model ids the endpoint listed.</returns>
    public static async Task<ConnectionTestResult> TestAsync(
        ProviderConfig provider,
        string? apiKey,
        HttpClient httpClient,
        TimeProvider? time,
        CancellationToken cancellationToken = default)
    {
        if (!provider.BaseUrl.IsAbsoluteUri
            || provider.BaseUrl.Scheme is not ("http" or "https")
            || !Uri.TryCreate(provider.BaseUrl.OriginalString.TrimEnd('/') + "/models", UriKind.Absolute, out var uri))
        {
            return Fail(ConnectionTestCode.InvalidBaseUrl, "The base URL is not a valid http or https address.");
        }

        using var clockTimeout = new CancellationTokenSource(Timeout, time ?? TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, clockTimeout.Token);

        try
        {
            var current = uri;
            for (var hop = 0; hop <= MaxSameOriginRedirects; hop++)
            {
                using var request = BuildRequest(provider, apiKey, current);
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (IsRedirect(response.StatusCode) && response.Headers.Location is { } location)
                {
                    if (Uri.TryCreate(current, location, out var next) && SameOriginOrUpgrade(uri, next))
                    {
                        current = next;
                        continue;
                    }

                    return DescribeStatus(response.StatusCode);
                }

                if (!response.IsSuccessStatusCode)
                {
                    return DescribeStatus(response.StatusCode);
                }

                var body = await ReadCappedAsync(response.Content, timeout.Token);
                return body is null
                    ? Fail(ConnectionTestCode.ModelListTooLarge, "The server's model list is too large to be an OpenAI-style answer.")
                    : ParseModels(body);
            }

            return DescribeStatus(HttpStatusCode.Redirect);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail(
                ConnectionTestCode.Timeout,
                $"The server did not answer within {Timeout.TotalSeconds:0} seconds. Check that it is running and the address is right.",
                new Dictionary<string, object?> { ["seconds"] = (int)Timeout.TotalSeconds });
        }
        catch (HttpRequestException ex)
        {
            return Fail(
                ConnectionTestCode.ConnectionFailed,
                $"Could not connect: {ex.Message}. Check that the server is running and the address is right.",
                new Dictionary<string, object?> { ["detail"] = ex.Message });
        }
    }

    private static HttpRequestMessage BuildRequest(ProviderConfig provider, string? apiKey, Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);

        // Replace rather than append, like the real requests do, so a header never ends up with two values.
        foreach (var (name, value) in provider.Headers ?? new Dictionary<string, string>())
        {
            request.Headers.Remove(name);
            request.Headers.TryAddWithoutValidation(name, value);
        }

        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        return request;
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static bool SameOriginOrUpgrade(Uri origin, Uri target) =>
        target.IsAbsoluteUri
        && target.Scheme is "http" or "https"
        && !(origin.Scheme == "https" && target.Scheme == "http")
        && string.Equals(origin.DnsSafeHost, target.DnsSafeHost, StringComparison.OrdinalIgnoreCase)
        && (origin.Port == target.Port || (origin.Scheme == "http" && origin.IsDefaultPort && target.Scheme == "https" && target.IsDefaultPort));

    private static async Task<string?> ReadCappedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaxBodyBytes)
        {
            return null;
        }

        var stream = await content.ReadAsStreamAsync(cancellationToken);
        await using (stream)
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaxBodyBytes)
                {
                    return null;
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }

            return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
    }

    private static ConnectionTestResult ParseModels(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var ids = new List<string>();
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Array)
            {
                for (var i = 0; i < data.GetArrayLength(); i++)
                {
                    var item = data[i];
                    if (item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty("id", out var id)
                        && id.ValueKind == JsonValueKind.String
                        && id.GetString() is { Length: > 0 } text)
                    {
                        ids.Add(text);
                    }
                }
            }

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids.Count == 0
                ? new ConnectionTestResult(true, ConnectionTestCode.ConnectedNoModels, "Connected, but the server listed no models. Add them manually.", ids)
                : new ConnectionTestResult(
                    true,
                    ConnectionTestCode.Connected,
                    $"Connected. The server lists {ids.Count} model{(ids.Count == 1 ? string.Empty : "s")}.",
                    ids,
                    new Dictionary<string, object?> { ["count"] = ids.Count });
        }
        catch (JsonException)
        {
            return Fail(ConnectionTestCode.NotAModelList, "The server answered, but not with an OpenAI-style model list. Check that the base URL points at the API (usually ending in /v1).");
        }
    }

    private static ConnectionTestResult DescribeStatus(HttpStatusCode status)
    {
        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => Fail(ConnectionTestCode.Unauthorized, "The server rejected the API key. Check the key, and that it can list models."),
            HttpStatusCode.NotFound => Fail(ConnectionTestCode.NotFound, "The server has no /models endpoint at this address. Check the base URL (usually ending in /v1), or add models manually."),
            HttpStatusCode.TooManyRequests => Fail(ConnectionTestCode.RateLimited, "The server is rate limiting requests. Try again shortly."),
            _ => Fail(
                ConnectionTestCode.HttpError,
                $"The server answered with HTTP {(int)status} ({status}).",
                new Dictionary<string, object?> { ["code"] = (int)status, ["status"] = status.ToString() }),
        };
    }

    private static ConnectionTestResult Fail(ConnectionTestCode code, string message, IReadOnlyDictionary<string, object?>? arguments = null)
    {
        return new ConnectionTestResult(false, code, message, [], arguments);
    }
}
