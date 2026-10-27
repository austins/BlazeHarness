using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BlazeHarness.Core.Mcp;

/// <summary>
/// Reads the standard <c>mcp.json</c> format: a top-level <c>mcpServers</c> (or <c>servers</c>) object of stdio and remote servers.
/// </summary>
/// <remarks>
/// A server with a problem is reported and left out, so one mistake doesn't hide the others. Fields the app doesn't know are ignored here and kept by <see cref="McpConfigEditor"/>.
/// </remarks>
public static partial class McpConfigParser
{
    /// <summary>
    /// Parse the text of an <c>mcp.json</c> file.
    /// </summary>
    /// <param name="json">The file's text. Blank text is an empty file.</param>
    /// <returns>The valid servers and the problems found.</returns>
    public static McpParseResult Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        if (string.IsNullOrWhiteSpace(json))
        {
            return new McpParseResult([], []);
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return Failed(ex is JsonException { LineNumber: { } line } ? $"The file is not valid JSON (line {line + 1})." : "The file is not valid JSON.");
        }

        if (root is not JsonObject top)
        {
            return Failed("The file must be a JSON object with a \"mcpServers\" object.");
        }

        if ((top["mcpServers"] ?? top["servers"]) is not { } servers)
        {
            return top.Count == 0 ? new McpParseResult([], []) : Failed("The file has no \"mcpServers\" object.");
        }

        if (servers is not JsonObject entries)
        {
            return Failed("\"mcpServers\" must be an object with one entry per server.");
        }

        List<McpServerConfig> parsed = [];
        List<string> errors = [];
        foreach (var (name, node) in entries)
        {
            var error = TryParseServer(name, node, out var server);
            if (error is null)
            {
                parsed.Add(server!);
            }
            else
            {
                errors.Add($"Server \"{name}\": {error}");
            }
        }

        return new McpParseResult(parsed, errors);
    }

    private static McpParseResult Failed(string error) => new([], [error]);

    private static string? TryParseServer(string name, JsonNode? node, out McpServerConfig? server)
    {
        server = null;
        if (!NamePattern().IsMatch(name) || name.Contains("__", StringComparison.Ordinal))
        {
            return "the name can only have letters, digits, - and single _ characters, up to 64 of them.";
        }

        if (node is not JsonObject fields)
        {
            return "it must be an object.";
        }

        string? command = null, cwd = null, url = null, type = null;
        List<string> args = [];
        Dictionary<string, string> env = new(StringComparer.Ordinal);
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        var disabled = false;

        var error = ReadString(fields, "command", ref command)
            ?? ReadString(fields, "cwd", ref cwd)
            ?? ReadString(fields, "url", ref url)
            ?? ReadString(fields, "type", ref type)
            ?? ReadStrings(fields, "args", args)
            ?? ReadMap(fields, "env", env)
            ?? ReadMap(fields, "headers", headers)
            ?? ReadBool(fields, "disabled", ref disabled);
        if (error is not null)
        {
            return error;
        }

        if (!TryGetTransport(type, command, url, out var transport, out error))
        {
            return error;
        }

        if (transport == McpTransport.Stdio)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return "a stdio server needs a \"command\".";
            }
        }
        else if (string.IsNullOrWhiteSpace(url))
        {
            return "a remote server needs a \"url\".";
        }
        else if (!url.Contains("${", StringComparison.Ordinal) && !IsWebAddress(url))
        {
            return "the \"url\" must be an http or https address.";
        }

        server = new McpServerConfig(name, transport, command, args, env, cwd, url, headers, disabled);
        return null;
    }

    private static bool TryGetTransport(string? type, string? command, string? url, out McpTransport transport, out string? error)
    {
        error = null;
        transport = McpTransport.Stdio;
        switch (type?.ToUpperInvariant())
        {
            case null or "":
                transport = command is null && url is not null ? McpTransport.Http : McpTransport.Stdio;
                return true;
            case "STDIO":
                return true;
            case "HTTP" or "STREAMABLE-HTTP" or "STREAMABLEHTTP":
                transport = McpTransport.Http;
                return true;
            case "SSE":
                transport = McpTransport.Sse;
                return true;
            default:
                error = $"the type \"{type}\" is not supported. Use stdio, http or sse.";
                return false;
        }
    }

    private static bool IsWebAddress(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private static string? ReadString(JsonObject fields, string key, ref string? value)
    {
        if (fields[key] is not { } node)
        {
            return null;
        }

        if (node is JsonValue v && v.TryGetValue<string>(out var text))
        {
            value = text;
            return null;
        }

        return $"\"{key}\" must be a string.";
    }

    private static string? ReadBool(JsonObject fields, string key, ref bool value)
    {
        if (fields[key] is not { } node)
        {
            return null;
        }

        if (node is JsonValue v && v.TryGetValue<bool>(out var flag))
        {
            value = flag;
            return null;
        }

        return $"\"{key}\" must be true or false.";
    }

    private static string? ReadStrings(JsonObject fields, string key, List<string> into)
    {
        if (fields[key] is not { } node)
        {
            return null;
        }

        if (node is not JsonArray array)
        {
            return $"\"{key}\" must be a list of strings.";
        }

        foreach (var item in array)
        {
            if (item is JsonValue v && v.TryGetValue<string>(out var text))
            {
                into.Add(text);
            }
            else
            {
                return $"\"{key}\" must be a list of strings.";
            }
        }

        return null;
    }

    private static string? ReadMap(JsonObject fields, string key, Dictionary<string, string> into)
    {
        if (fields[key] is not { } node)
        {
            return null;
        }

        if (node is not JsonObject map)
        {
            return $"\"{key}\" must be an object of strings.";
        }

        foreach (var (name, item) in map)
        {
            if (item is JsonValue v && v.TryGetValue<string>(out var text))
            {
                into[name] = text;
            }
            else
            {
                return $"\"{key}\" must be an object of strings, but \"{name}\" is not a string.";
            }
        }

        return null;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NamePattern();
}
