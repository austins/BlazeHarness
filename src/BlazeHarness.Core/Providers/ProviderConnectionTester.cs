using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace BlazeHarness.Core.Providers;

/// <summary>
/// Calls a provider's <c>/models</c> endpoint to check the address and key, and to list the models it serves.
/// </summary>
public static class ProviderConnectionTester
{
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
    public static async Task<ConnectionTestResult> TestAsync(
        ProviderConfig provider,
        string? apiKey,
        HttpClient httpClient,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(provider.BaseUrl.TrimEnd('/') + "/models", UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return Fail("The base URL is not a valid http or https address.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        foreach (var (name, value) in provider.Headers ?? new Dictionary<string, string>())
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            using var response = await httpClient.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return Fail(DescribeStatus(response.StatusCode));
            }

            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return ParseModels(body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail("The server did not answer within 15 seconds. Check that it is running and the address is right.");
        }
        catch (HttpRequestException ex)
        {
            return Fail($"Could not connect: {ex.Message}. Check that the server is running and the address is right.");
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
            var message = ids.Count == 0
                ? "Connected, but the server listed no models. Add them manually."
                : $"Connected. The server lists {ids.Count} model{(ids.Count == 1 ? string.Empty : "s")}.";
            return new ConnectionTestResult(true, message, ids);
        }
        catch (JsonException)
        {
            return Fail("The server answered, but not with an OpenAI-style model list. Check that the base URL points at the API (usually ending in /v1).");
        }
    }

    private static string DescribeStatus(HttpStatusCode status)
    {
        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "The server rejected the API key. Check the key, and that it can list models.",
            HttpStatusCode.NotFound => "The server has no /models endpoint at this address. Check the base URL (usually ending in /v1), or add models manually.",
            HttpStatusCode.TooManyRequests => "The server is rate limiting requests. Try again shortly.",
            _ => $"The server answered with HTTP {(int)status} ({status}).",
        };
    }

    private static ConnectionTestResult Fail(string message)
    {
        return new ConnectionTestResult(false, message, []);
    }
}
