using BlazeHarness.Core.Tools;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BlazeHarness.Core.Mcp;

/// <summary>
/// Searches the tools of the MCP servers that are not listed in the request, and returns what the model needs to call them.
/// </summary>
internal sealed class FindToolsTool : ITool
{
    public const string ToolName = "find_tools";

    private const int DefaultLimit = 5;
    private const int MaxLimit = 20;
    private const int MaxRelated = 10;
    private const int DefaultListing = 60;
    private const int MaxListing = 100;
    private const int InventoryDescriptionCharacters = 100;

    // A match far behind the best one is noise, not an alternative.
    private const double MinimumShareOfBest = 0.35;

    private readonly McpManager _manager;
    private readonly IReadOnlyList<string> _servers;

    public FindToolsTool(McpManager manager, IReadOnlyList<string> servers)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(servers);
        _manager = manager;
        _servers = servers;
    }

    public string Name => ToolName;

    // The servers are named here and nowhere else, so the request does not change when a server connects.
    public string Description =>
        $"Find tools that are not listed in this request. They belong to these MCP servers: {string.Join(", ", _servers)}. " +
        "Describe what you need in a few English keywords, for example \"search reddit posts\", and add \"server\" to search only one. " +
        "It returns the best matches with their descriptions and input schemas; call one with use_tool. " +
        "With only \"server\" it lists that server's tools a page at a time; with no arguments it lists the servers.";

    public string InputSchema => $$"""
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "A few English keywords for what the tool should do." },
            "server": { "type": "string", "description": "Search only this server." },
            "limit": { "type": "integer", "minimum": 1, "maximum": {{MaxListing}}, "description": "The most results to return. A search gives {{DefaultLimit}} when left out and at most {{MaxLimit}}; a list of a server's tools gives {{DefaultListing}} when left out and at most {{MaxListing}}." },
            "offset": { "type": "integer", "minimum": 0, "description": "When listing a server's tools, how many to skip, to read the next page." }
          }
        }
        """;

    public bool IsReadOnly => true;

    public TimeSpan Timeout { get; } = TimeSpan.FromMinutes(2);

    public string Summarize(JsonElement arguments)
    {
        var query = ToolSupport.Text(arguments, "query");
        return query.Length > 0 ? query : ToolSupport.Text(arguments, "server");
    }

    public async Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var query = arguments.OptionalString("query")?.Trim() ?? string.Empty;
        var serverName = arguments.OptionalString("server")?.Trim() ?? string.Empty;
        var requested = arguments.OptionalInt("limit", 1, MaxListing);
        var offset = arguments.OptionalInt("offset", 0, int.MaxValue) ?? 0;

        IReadOnlyList<string> targets = _servers;
        if (serverName.Length > 0)
        {
            if (_servers.FirstOrDefault(s => string.Equals(s, serverName, StringComparison.OrdinalIgnoreCase)) is not { } match)
            {
                return ToolResult.Error($"There is no server named '{serverName}'. The servers are: {string.Join(", ", _servers)}.");
            }

            targets = [match];
        }
        else if (query.Length == 0)
        {
            return ToolResult.Ok(await OverviewAsync(cancellationToken));
        }

        var listings = await Task.WhenAll(targets.Select(t => _manager.ListAsync(t, connect: true, cancellationToken)));
        var tools = listings.SelectMany(l => l.Tools).ToList();
        var output = new StringBuilder();
        foreach (var failed in listings.Where(l => l.Error is not null))
        {
            output.Append("The server '").Append(failed.Server).Append("' is not available: ").AppendLine(failed.Error);
        }

        if (tools.Count == 0)
        {
            output.Append("There are no tools to find in ").Append(serverName.Length > 0 ? "that server." : "these servers.");
            return ToolResult.Ok(output.ToString());
        }

        if (query.Length == 0)
        {
            AppendInventory(output, tools, offset, requested ?? DefaultListing, paged: serverName.Length > 0);
            return ToolResult.Ok(output.ToString());
        }

        var ranked = McpToolRanker.Rank(tools, query);
        if (ranked.Count == 0)
        {
            output.Append("No tool matches '").Append(query).AppendLine("'. Try other keywords in English, or pick one of these:");
            AppendInventory(output, tools, 0, DefaultListing, paged: serverName.Length > 0);
            return ToolResult.Ok(output.ToString());
        }

        var best = ranked[0].Score;
        var found = ranked.Where(r => r.Score >= best * MinimumShareOfBest).Take(Math.Min(requested ?? DefaultLimit, MaxLimit)).ToList();
        output.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Found {found.Count} {(found.Count == 1 ? "tool" : "tools")}. Call one with use_tool, using its name exactly."));
        foreach (var (tool, _) in found)
        {
            output.AppendLine().Append("## ").AppendLine(tool.Name);
            if (tool.Description.Length > 0)
            {
                output.AppendLine(tool.Description);
            }

            output.Append("Input schema: ").AppendLine(tool.InputSchema);
        }

        var related = ranked.Select(r => r.Tool).Except(found.Select(f => f.Tool)).Take(MaxRelated).Select(t => t.Name).ToList();
        if (related.Count > 0)
        {
            output.AppendLine().Append("Also related: ").AppendLine(string.Join(", ", related));
        }

        return ToolResult.Ok(output.ToString());
    }

    private async Task<string> OverviewAsync(CancellationToken cancellationToken)
    {
        var listings = await Task.WhenAll(_servers.Select(s => _manager.ListAsync(s, connect: false, cancellationToken)));
        var servers = listings.Select(l => l.Known ? string.Create(CultureInfo.InvariantCulture, $"{l.Server} ({l.Tools.Count} {(l.Tools.Count == 1 ? "tool" : "tools")})") : l.Server);
        return $"Servers with tools to find: {string.Join(", ", servers)}. Search with a query, or name a server to list its tools.";
    }

    private static void AppendInventory(StringBuilder output, List<McpToolListing> tools, int offset, int limit, bool paged)
    {
        var page = tools.Skip(offset).Take(limit).ToList();
        if (page.Count == 0)
        {
            output.AppendLine(string.Create(CultureInfo.InvariantCulture, $"There are only {tools.Count} tools, so nothing is left after {offset}."));
            return;
        }

        foreach (var tool in page)
        {
            output.Append("- ").Append(tool.Name);
            if (tool.Description.Length > 0)
            {
                var text = tool.Description;
                var stop = text.IndexOf(". ", StringComparison.Ordinal);
                text = stop > 0 ? text[..(stop + 1)] : text;
                output.Append(": ").Append(text.Length <= InventoryDescriptionCharacters ? text : text[..(InventoryDescriptionCharacters - 1)].TrimEnd() + "…");
            }

            output.AppendLine();
        }

        var end = offset + page.Count;
        if (end < tools.Count)
        {
            output.AppendLine(paged
                ? string.Create(CultureInfo.InvariantCulture, $"Showing {offset + 1}-{end} of {tools.Count}. For the next page, call find_tools with this server and offset {end}.")
                : string.Create(CultureInfo.InvariantCulture, $"…and {tools.Count - end} more. Search with a query, or name a server to list its tools."));
        }
    }
}
