using BlazeHarness.Core.Tools;

namespace BlazeHarness.Core.Mcp;

public sealed partial class McpManager
{
    /// <summary>
    /// Get what a chat offers the model from the given servers: the tools set to always load, and two tools that find and call the rest.
    /// </summary>
    /// <remarks>
    /// Only a server that has tools set to always load is started here. The other servers start when the model first searches them or calls one of their tools.
    /// </remarks>
    /// <param name="servers">The names of the servers the chat uses.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>The tools to declare; the search tools come last and only when some server has tools left to search.</returns>
    internal async Task<IReadOnlyList<ITool>> OfferAsync(IEnumerable<string> servers, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(servers);
        var wanted = servers.Distinct(StringComparer.Ordinal).ToList();
        var configs = _config.Servers.Where(s => wanted.Contains(s.Name)).OrderBy(s => s.Name, StringComparer.Ordinal).ToList();

        var upFront = configs.Where(c => c.AlwaysLoad.Count > 0).ToDictionary(c => c.Name, StringComparer.Ordinal);
        List<ITool> offer = [.. (await ToolsAsync(upFront.Keys, cancellationToken)).OfType<McpTool>().Where(t => upFront[t.ServerName].LoadsAlways(t.RemoteName))];

        var searchable = configs.Where(c => !c.AlwaysLoad.Contains("*")).Select(c => c.Name).ToList();
        if (searchable.Count > 0)
        {
            offer.Add(new FindToolsTool(this, searchable));
            offer.Add(new UseToolTool(this, searchable));
        }

        return offer;
    }

    /// <summary>
    /// List the tools of a server that are found with a search.
    /// </summary>
    /// <param name="server">The server name.</param>
    /// <param name="connect">Whether to start the server when neither a connection nor an earlier run knows its tools.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>The tools, or the reason there are none to list.</returns>
    internal async Task<McpServerListing> ListAsync(string server, bool connect, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (_config.Servers.FirstOrDefault(s => s.Name == server) is not { } config)
        {
            return new McpServerListing(server, [], "There is no such server.", Known: false);
        }

        var infos = ConnectedTools(config) ?? _toolCache?.Get(config);
        if (infos is null)
        {
            if (!connect)
            {
                return new McpServerListing(server, [], null, Known: false);
            }

            await EnsureConnectedAsync(config, cancellationToken);
            infos = ConnectedTools(config);
            if (infos is null)
            {
                return new McpServerListing(server, [], StatusOf(config).Error ?? "The server is not available.", Known: false);
            }
        }

        var taken = new HashSet<string>(StringComparer.Ordinal);
        List<McpToolListing> tools = [];
        foreach (var info in infos.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            var name = McpToolNames.Build(server, info.Name, taken);
            if (!config.IsExcluded(info.Name) && !config.LoadsAlways(info.Name))
            {
                tools.Add(new McpToolListing(name, server, info.Name, McpTool.TrimDescription(info.Description), McpTool.NormalizeSchema(info.InputSchema)));
            }
        }

        return new McpServerListing(server, tools, null, Known: true);
    }

    /// <summary>
    /// Find a tool by the name the model calls it by, starting its server if needed.
    /// </summary>
    /// <param name="name">The tool name, such as <c>server__tool</c>.</param>
    /// <param name="servers">The servers the chat uses; a tool of any other server is not found.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>The tool, or the reason it can't be called.</returns>
    internal async Task<McpToolLookup> FindToolAsync(string name, IReadOnlyCollection<string> servers, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(servers);
        if (!McpToolNames.TrySplit(name, out var server, out _) || !servers.Contains(server))
        {
            return new McpToolLookup(null, $"There is no tool named '{name}'. Use find_tools to get the exact name.");
        }

        var tool = (await ToolsAsync([server], cancellationToken)).OfType<McpTool>().FirstOrDefault(t => t.Name == name);
        if (tool is not null)
        {
            return new McpToolLookup(tool, null);
        }

        return _config.Servers.FirstOrDefault(s => s.Name == server) is { } config && StatusOf(config) is { State: McpServerState.Failed, Error: { } error }
            ? new McpToolLookup(null, $"The server '{server}' is not running: {error}")
            : new McpToolLookup(null, $"There is no tool named '{name}'. Use find_tools to get the exact name.");
    }

    private IReadOnlyList<McpToolInfo>? ConnectedTools(McpServerConfig config)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(config.Name, out var entry) && entry.State == McpServerState.Connected && config.SameAs(entry.Config) ? entry.Tools : null;
        }
    }
}
