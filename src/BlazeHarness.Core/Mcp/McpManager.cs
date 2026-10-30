using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Secrets;
using BlazeHarness.Core.Tools;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace BlazeHarness.Core.Mcp;

/// <summary>
/// Keeps the connections to the MCP servers in <c>mcp.json</c>, which every chat shares.
/// </summary>
/// <remarks>
/// Servers start the first time a chat that has them turned on needs their tools, not at startup.
/// A server that changed in <c>mcp.json</c> is stopped, and starts again with its new settings when next needed.
/// </remarks>
public sealed partial class McpManager : IAsyncDisposable
{
    internal static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(30);

    private readonly McpConfigFile _config;
    private readonly IMcpConnector _connector;
    private readonly ILogger _log;
    private readonly Func<string, string?> _environment;
    private readonly TimeProvider _time;
    private readonly TimeSpan _connectTimeout;
    private readonly PermissionDefaults? _permissions;
    private readonly ISecretStore? _secrets;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _reconcile = new(1, 1);
    private Dictionary<string, McpServerConfig> _known;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpManager"/> class.
    /// </summary>
    /// <param name="config">The <c>mcp.json</c> file.</param>
    /// <param name="connector">Connects to servers.</param>
    /// <param name="log">The logger.</param>
    /// <param name="time">The clock, used to decide when to retry a failed server.</param>
    /// <param name="environment">Looks up the variables that <c>${NAME}</c> references name; defaults to the process environment.</param>
    /// <param name="connectTimeout">How long a server may take to start; defaults to one minute, which leaves room for a first download.</param>
    /// <param name="permissions">Where trust in a server is kept, so it can be withdrawn when the server's settings change; when null, trust is left alone.</param>
    /// <param name="secrets">Where the secrets of a server's environment and headers are kept; when null, only environment variables fill in a reference.</param>
    public McpManager(
        McpConfigFile config,
        IMcpConnector connector,
        ILogger<McpManager> log,
        TimeProvider time,
        Func<string, string?>? environment = null,
        TimeSpan? connectTimeout = null,
        PermissionDefaults? permissions = null,
        ISecretStore? secrets = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(time);
        _config = config;
        _connector = connector;
        _log = log;
        _environment = environment ?? Environment.GetEnvironmentVariable;
        _time = time;
        _connectTimeout = connectTimeout ?? TimeSpan.FromMinutes(1);
        _permissions = permissions;
        _secrets = secrets;
        _known = _config.Servers.ToDictionary(s => s.Name, StringComparer.Ordinal);
        _config.Changed += OnConfigChanged;
    }

    /// <summary>
    /// Raised, on any thread, when a server's state or the configured servers changed.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Get the state of every server in <c>mcp.json</c>, in file order.
    /// </summary>
    /// <returns>The servers, including the ones turned off.</returns>
    public IReadOnlyList<McpServerStatus> Statuses()
    {
        return [.. _config.Servers.Select(StatusOf)];
    }

    /// <summary>
    /// Work out whether a chat has a server turned on: its own choice wins, else the server's global state.
    /// </summary>
    /// <param name="server">The server.</param>
    /// <param name="overrides">The chat's choices by server name, or null.</param>
    /// <returns>True when the chat uses the server.</returns>
    public static bool IsEnabled(McpServerConfig server, IReadOnlyDictionary<string, bool>? overrides)
    {
        ArgumentNullException.ThrowIfNull(server);
        return overrides is not null && overrides.TryGetValue(server.Name, out var chosen) ? chosen : !server.Disabled;
    }

    /// <summary>
    /// Connect to a server now, stopping any earlier connection, as the "Test connection" button does.
    /// </summary>
    /// <param name="name">The server name.</param>
    /// <param name="cancellationToken">A token to cancel the attempt.</param>
    /// <returns>The state afterwards, which is <see cref="McpServerState.Failed"/> with the reason when it did not work; null when there is no such server.</returns>
    public async Task<McpServerStatus?> ReconnectAsync(string name, CancellationToken cancellationToken = default)
    {
        if (_config.Servers.FirstOrDefault(s => s.Name == name) is not { } config)
        {
            return null;
        }

        var entry = EntryFor(config);
        await entry.Gate.WaitAsync(cancellationToken);
        try
        {
            if (!IsCurrent(entry))
            {
                return null;
            }

            await StopAsync(entry);
            await ConnectCoreAsync(entry, config, cancellationToken);
        }
        finally
        {
            entry.Gate.Release();
        }

        return StatusOf(config);
    }

    /// <summary>
    /// Stop a server's connection, if it has one.
    /// </summary>
    /// <param name="name">The server name.</param>
    /// <returns>A task that completes once the server has stopped.</returns>
    public async Task StopAsync(string name)
    {
        Entry? entry;
        lock (_gate)
        {
            _entries.TryGetValue(name, out entry);
        }

        if (entry is null)
        {
            return;
        }

        await entry.Gate.WaitAsync();
        try
        {
            await StopAsync(entry);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    /// <summary>
    /// Get the tools of the given servers, starting the ones that are not running.
    /// </summary>
    /// <param name="servers">The names of the servers to use.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>The tools, named so no two clash; servers that cannot start are left out, and say why in their status.</returns>
    public async Task<IReadOnlyList<ITool>> ToolsAsync(IEnumerable<string> servers, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(servers);
        var wanted = servers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var configs = _config.Servers.Where(s => wanted.Contains(s.Name)).OrderBy(s => s.Name, StringComparer.Ordinal).ToList();
        await Task.WhenAll(configs.Select(c => EnsureConnectedAsync(c, cancellationToken)));

        var taken = new HashSet<string>(StringComparer.Ordinal);
        List<ITool> tools = [];
        foreach (var serverName in configs.Select(c => c.Name))
        {
            Entry? entry;
            lock (_gate)
            {
                _entries.TryGetValue(serverName, out entry);
            }

            if (entry is not { State: McpServerState.Connected, Connection: { } connection })
            {
                continue;
            }

            foreach (var info in entry.Tools.OrderBy(t => t.Name, StringComparer.Ordinal))
            {
                tools.Add(new McpTool(McpToolNames.Build(serverName, info.Name, taken), serverName, info, connection));
            }
        }

        return tools;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        List<Entry> entries;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            entries = [.. _entries.Values];
            _entries.Clear();
        }

        _config.Changed -= OnConfigChanged;
        foreach (var entry in entries)
        {
            await CloseConnectionAsync(entry);
        }

        _reconcile.Dispose();
    }

    /// <summary>
    /// Stop the servers whose settings changed or that were removed from <c>mcp.json</c>.
    /// </summary>
    /// <returns>A task that completes once the changed servers have stopped.</returns>
    internal async Task ReconcileAsync()
    {
        await _reconcile.WaitAsync();
        try
        {
            var current = _config.Servers.ToDictionary(s => s.Name, StringComparer.Ordinal);
            foreach (var (name, _) in _known.Where(p => !current.TryGetValue(p.Key, out var now) || !now.SameAs(p.Value)))
            {
                await WithdrawTrustAsync(name);
            }

            _known = current;
            List<Entry> stale;
            lock (_gate)
            {
                stale = [.. _entries.Values.Where(e => !current.TryGetValue(e.Name, out var now) || !now.SameAs(e.Config))];
                foreach (var entry in stale.Where(e => !current.ContainsKey(e.Name)))
                {
                    _entries.Remove(entry.Name);
                }
            }

            foreach (var entry in stale)
            {
                await entry.Gate.WaitAsync();
                try
                {
                    await StopAsync(entry);
                    Reset(entry, current.TryGetValue(entry.Name, out var now) ? now : entry.Config);
                }
                finally
                {
                    entry.Gate.Release();
                }
            }
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        finally
        {
            try
            {
                _reconcile.Release();
            }
            catch (ObjectDisposedException)
            {
                // The manager was disposed while this ran.
            }
        }

        RaiseChanged();
    }

    private static McpServerStatus Describe(McpServerConfig config, Entry? entry)
    {
        return entry is null || !config.SameAs(entry.Config)
            ? new McpServerStatus(config, McpServerState.Stopped, null, [])
            : new McpServerStatus(config, entry.State, entry.Error, entry.Tools);
    }

    private McpServerStatus StatusOf(McpServerConfig config)
    {
        lock (_gate)
        {
            return Describe(config, _entries.GetValueOrDefault(config.Name));
        }
    }

    private bool IsCurrent(Entry entry)
    {
        lock (_gate)
        {
            return IsCurrentLocked(entry);
        }
    }

    private bool IsCurrentLocked(Entry entry) => !_disposed && _entries.TryGetValue(entry.Name, out var live) && ReferenceEquals(live, entry);

    private void Reset(Entry entry, McpServerConfig config)
    {
        lock (_gate)
        {
            entry.Reset(config);
        }
    }

    private Entry EntryFor(McpServerConfig config)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_entries.TryGetValue(config.Name, out var entry))
            {
                entry = new Entry(config);
                _entries[config.Name] = entry;
            }

            return entry;
        }
    }

    private async Task EnsureConnectedAsync(McpServerConfig config, CancellationToken cancellationToken)
    {
        var entry = EntryFor(config);
        await entry.Gate.WaitAsync(cancellationToken);
        try
        {
            // A server removed from mcp.json, or a disposed manager, must not start a process nobody will stop.
            if (!IsCurrent(entry))
            {
                return;
            }

            if (!config.SameAs(entry.Config))
            {
                await StopAsync(entry);
                Reset(entry, config);
            }

            var retry = entry.State == McpServerState.Failed && _time.GetUtcNow() - entry.FailedAt >= RetryAfterFailure;
            if (entry.State == McpServerState.Stopped || retry)
            {
                await ConnectCoreAsync(entry, config, cancellationToken);
            }
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    private async Task ConnectCoreAsync(Entry entry, McpServerConfig config, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            entry.Reset(config);
            entry.State = McpServerState.Connecting;
        }

        RaiseChanged();

        Func<string, string?> lookup;
        try
        {
            lookup = await LookupAsync(config, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSecretsFailed(ex.GetType().Name, config.Name);
            Fail(entry, "The secret store could not be read.");
            return;
        }

        if (!config.TryExpand(lookup, out var expanded, out var problem))
        {
            Fail(entry, problem);
            return;
        }

        using var timeout = new CancellationTokenSource(_connectTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        IMcpConnection? connection = null;
        try
        {
            connection = await _connector.ConnectAsync(expanded, linked.Token);
            var tools = await connection.ListToolsAsync(linked.Token);
            bool kept;
            lock (_gate)
            {
                kept = IsCurrentLocked(entry);
                if (kept)
                {
                    entry.Connection = connection;
                    entry.Tools = tools;
                    entry.State = McpServerState.Connected;
                }
            }

            if (!kept)
            {
                await DisposeQuietlyAsync(connection);
                return;
            }

            _ = WatchAsync(entry, connection);
            RaiseChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await DisposeQuietlyAsync(connection);
            lock (_gate)
            {
                entry.State = McpServerState.Stopped;
            }

            RaiseChanged();
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await DisposeQuietlyAsync(connection);
            LogConnectFailed(ex, config.Name);
            Fail(entry, ex is OperationCanceledException
                ? string.Create(CultureInfo.InvariantCulture, $"The server did not answer within {_connectTimeout.TotalSeconds:0} seconds.")
                : ex.Message);
        }
    }

    private async Task WatchAsync(Entry entry, IMcpConnection connection)
    {
        try
        {
#pragma warning disable VSTHRD003 // The connection's own signal that it ended.
            await connection.Closed;
#pragma warning restore VSTHRD003
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogConnectionEnded(ex, entry.Name);
        }

        var ended = false;
        lock (_gate)
        {
            if (ReferenceEquals(entry.Connection, connection))
            {
                entry.Connection = null;
                entry.Tools = [];
                entry.State = McpServerState.Failed;
                entry.Error = "The server stopped.";
                entry.FailedAt = _time.GetUtcNow();
                ended = true;
            }
        }

        if (ended)
        {
            RaiseChanged();
        }
    }

    private void Fail(Entry entry, string error)
    {
        lock (_gate)
        {
            entry.State = McpServerState.Failed;
            entry.Error = error;
            entry.FailedAt = _time.GetUtcNow();
        }

        RaiseChanged();
    }

    private async Task StopAsync(Entry entry)
    {
        await CloseConnectionAsync(entry);
        lock (_gate)
        {
            entry.State = McpServerState.Stopped;
            entry.Error = null;
            entry.Tools = [];
        }

        RaiseChanged();
    }

    private Task CloseConnectionAsync(Entry entry)
    {
        IMcpConnection? connection;
        lock (_gate)
        {
            connection = entry.Connection;
            entry.Connection = null;
        }

        return DisposeQuietlyAsync(connection);
    }

    private async Task DisposeQuietlyAsync(IMcpConnection? connection)
    {
        if (connection is null)
        {
            return;
        }

        try
        {
#pragma warning disable IDISP007 // The manager owns the connections it opened.
            await connection.DisposeAsync();
#pragma warning restore IDISP007
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogDisposeFailed(ex);
        }
    }

    // Trust was given to what the server was when the user chose it, not to whatever the file says next.
    private async Task WithdrawTrustAsync(string name)
    {
        if (_permissions?.IsMcpServerTrusted(name) == true)
        {
            await _permissions.SetMcpServerTrustedAsync(name, trusted: false);
        }
    }

    private async Task<Func<string, string?>> LookupAsync(McpServerConfig config, CancellationToken cancellationToken)
    {
        if (_secrets is null)
        {
            return _environment;
        }

        // A variable that is set in the environment wins over the secret store.
        Dictionary<string, string> found = new(StringComparer.Ordinal);
        foreach (var variable in McpSecretVariables.Referenced(config).Where(v => _environment(v) is null))
        {
            if (await _secrets.GetAsync(McpSecretVariables.StoreKey(variable), cancellationToken) is { } value)
            {
                found[variable] = value;
            }
        }

        return name => _environment(name) ?? found.GetValueOrDefault(name);
    }

    private void OnConfigChanged(object? sender, EventArgs e)
    {
        _ = ReconcileAsync();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not connect to the MCP server {Server}.")]
    private partial void LogConnectFailed(Exception ex, string server);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the secrets of the MCP server {Server}: {ExceptionType}.")]
    private partial void LogSecretsFailed(string exceptionType, string server);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The connection to the MCP server {Server} ended with an error.")]
    private partial void LogConnectionEnded(Exception ex, string server);

    [LoggerMessage(Level = LogLevel.Debug, Message = "An MCP connection did not close cleanly.")]
    private partial void LogDisposeFailed(Exception ex);

    private sealed class Entry(McpServerConfig config)
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public string Name => Config.Name;

        public McpServerConfig Config { get; private set; } = config;

        public McpServerState State { get; set; }

        public string? Error { get; set; }

        public DateTimeOffset FailedAt { get; set; }

        public IMcpConnection? Connection { get; set; }

        public IReadOnlyList<McpToolInfo> Tools { get; set; } = [];

        public void Reset(McpServerConfig next)
        {
            Config = next;
            State = McpServerState.Stopped;
            Error = null;
            Tools = [];
        }
    }
}
