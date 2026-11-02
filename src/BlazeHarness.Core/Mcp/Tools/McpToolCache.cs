using BlazeHarness.Core.Config;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace BlazeHarness.Core.Mcp.Tools;

/// <summary>
/// Remembers what each MCP server offered when it last connected, so tools can be searched and sized without starting the server.
/// </summary>
/// <remarks>
/// An entry only answers for the settings it was stored under: a server whose command, address or environment changed has no entry until it connects again.
/// </remarks>
public sealed class McpToolCache : IDisposable
{
    private readonly JsonFileStore<McpToolCacheDocument> _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpToolCache"/> class.
    /// </summary>
    /// <param name="paths">Where the cache file lives.</param>
    /// <param name="loggers">Creates the logger of the file store.</param>
    /// <param name="time">The clock of the file store.</param>
    public McpToolCache(AppPaths paths, ILoggerFactory loggers, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(loggers);
        ArgumentNullException.ThrowIfNull(time);
        _store = new JsonFileStore<McpToolCacheDocument>(paths.McpToolsFile, loggers.CreateLogger<JsonFileStore<McpToolCacheDocument>>(), time, watch: false);
    }

    /// <summary>
    /// Get the tools a server listed the last time it connected with its current settings.
    /// </summary>
    /// <param name="server">The server.</param>
    /// <returns>The tools, or null when the server has not connected since its settings last changed.</returns>
    public IReadOnlyList<McpToolInfo>? Get(McpServerConfig server)
    {
        ArgumentNullException.ThrowIfNull(server);
        return _store.Current.Servers.TryGetValue(server.Name, out var entry) && entry.Fingerprint == Fingerprint(server) ? entry.Tools : null;
    }

    /// <summary>
    /// Remember the tools a server just listed, forgetting servers that are no longer in <c>mcp.json</c>.
    /// </summary>
    /// <param name="server">The server.</param>
    /// <param name="tools">The tools it listed.</param>
    /// <param name="configured">The names of the servers in <c>mcp.json</c>.</param>
    /// <returns>A task that completes once the cache is saved.</returns>
    public Task SetAsync(McpServerConfig server, IReadOnlyList<McpToolInfo> tools, IReadOnlySet<string> configured)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(configured);
        var fingerprint = Fingerprint(server);
        if (_store.Current.Servers.TryGetValue(server.Name, out var current) && current.Fingerprint == fingerprint && current.Tools.SequenceEqual(tools))
        {
            return Task.CompletedTask;
        }

        return _store.UpdateAsync(document =>
        {
            var servers = document.Servers.Where(p => p.Key != server.Name && configured.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            servers[server.Name] = new McpToolCacheEntry(fingerprint, tools);
            return document with { Servers = servers };
        });
    }

    /// <inheritdoc />
    public void Dispose() => _store.Dispose();

    // Covers what decides which server starts, as McpServerConfig.SameAs does, using the values as written so no secret is read.
    private static string Fingerprint(McpServerConfig server)
    {
        var text = new StringBuilder()
            .Append(server.Transport).Append('\0')
            .Append(server.Command).Append('\0')
            .Append(server.Cwd).Append('\0')
            .Append(server.Url).Append('\0');
        foreach (var arg in server.Args)
        {
            text.Append(arg).Append('\0');
        }

        foreach (var (key, value) in server.Env.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            text.Append(key).Append('=').Append(value).Append('\0');
        }

        foreach (var (key, value) in server.Headers.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            text.Append(key).Append('=').Append(value).Append('\0');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
