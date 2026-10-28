using System.Text.Json;

namespace BlazeHarness.Core.Web.Search.Providers;

/// <summary>
/// Reads the reply of a hosted search service that speaks the MCP <c>tools/call</c> shape over plain HTTP.
/// </summary>
/// <remarks>
/// The reply is either a JSON object or a server-sent event stream whose last <c>data:</c> line holds that object.
/// </remarks>
internal static class McpSearchResponse
{
    /// <summary>
    /// Build the request body of a <c>tools/call</c>.
    /// </summary>
    /// <param name="tool">The remote tool name.</param>
    /// <param name="arguments">The tool arguments, serialized as JSON.</param>
    /// <returns>The JSON-RPC body.</returns>
    public static string CallBody(string tool, object arguments)
    {
        return JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = tool, arguments } });
    }

    /// <summary>
    /// Read the text blocks of a reply.
    /// </summary>
    /// <param name="body">The response body.</param>
    /// <returns>The text of each text block.</returns>
    /// <exception cref="SearchProviderException">The reply is an error or has no text.</exception>
    public static IReadOnlyList<string> TextBlocks(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(Payload(body));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null))
            {
                throw new SearchProviderException(SearchFailure.BadResponse);
            }

            if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
            {
                throw new SearchProviderException(SearchFailure.BadResponse);
            }

            if (result.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True)
            {
                throw new SearchProviderException(SearchFailure.Unavailable);
            }

            List<string> blocks = [];
            if (result.TryGetProperty("content", out var content))
            {
                foreach (var item in JsonItems.Of(content))
                {
                    if (JsonItems.Text(item, "type") == "text" && JsonItems.Text(item, "text") is { Length: > 0 } value)
                    {
                        blocks.Add(value);
                    }
                }
            }

            return blocks.Count > 0 ? blocks : throw new SearchProviderException(SearchFailure.BadResponse);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new SearchProviderException(SearchFailure.BadResponse);
        }
    }

    private static string Payload(string body)
    {
        var trimmed = body.AsSpan().TrimStart();
        if (trimmed.StartsWith("{"))
        {
            return body;
        }

        string? last = null;
        foreach (var raw in body.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("data:", StringComparison.Ordinal) && line[5..].Trim() is { Length: > 0 } data)
            {
                last = data;
            }
        }

        return last ?? body;
    }
}
