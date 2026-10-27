using BlazeHarness.Core.Config;
using BlazeHarness.Core.Providers;

namespace BlazeHarness.Core.Permissions;

/// <summary>
/// The global choices about permissions: what new chats start with, and whether the risky options exist at all.
/// </summary>
/// <remarks>
/// A new chat never starts <see cref="AccessLevel.Unrestricted"/> or <see cref="ApprovalMode.NeverAsk"/>, whatever the config file says,
/// so those are only ever turned on by the user, for one chat.
/// </remarks>
public sealed class PermissionDefaults
{
    private readonly JsonFileStore<AppConfig> _config;
    private readonly AppPaths _paths;

    /// <summary>
    /// Initializes a new instance of the <see cref="PermissionDefaults"/> class.
    /// </summary>
    /// <param name="config">The store of the app configuration file.</param>
    /// <param name="paths">The app's folders.</param>
    public PermissionDefaults(JsonFileStore<AppConfig> config, AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(paths);
        _config = config;
        _paths = paths;
    }

    /// <summary>
    /// Gets the settings a chat that works in a folder starts with.
    /// </summary>
    public PermissionSettings Current
    {
        get
        {
            var config = _config.Current;
            return new PermissionSettings(
                config.DefaultAccess is AccessLevel.ReadOnly ? AccessLevel.ReadOnly : PermissionSettings.Default.Access,
                config.DefaultApprovals is ApprovalMode.AutoApprove ? ApprovalMode.AutoApprove : PermissionSettings.Default.Approvals);
        }
    }

    /// <summary>
    /// Gets a value indicating whether a chat may use <see cref="AccessLevel.Unrestricted"/> and <see cref="ApprovalMode.NeverAsk"/>.
    /// </summary>
    public bool AllowBypass => !_config.Current.DisableBypassModes;

    /// <summary>
    /// Gets the rules the user wrote, in the order they apply.
    /// </summary>
    public IReadOnlyList<PermissionRule> Rules => _config.Current.PermissionRules;

    /// <summary>
    /// Gets the folders no tool may touch, besides the app's own folder, which is always protected.
    /// </summary>
    public IReadOnlyList<string> ProtectedFolders => _config.Current.ProtectedFolders ?? SensitivePaths.DefaultFolders(_paths);

    /// <summary>
    /// Gets a value indicating whether the protected folders are the built-in list.
    /// </summary>
    public bool UsesDefaultProtectedFolders => _config.Current.ProtectedFolders is null;

    /// <summary>
    /// Gets the names of the MCP servers the user trusts.
    /// </summary>
    public IReadOnlyList<string> TrustedMcpServers => _config.Current.TrustedMcpServers;

    /// <summary>
    /// Get whether the user trusts an MCP server.
    /// </summary>
    /// <param name="serverName">The server's name in <c>mcp.json</c>.</param>
    /// <returns>True when the server's read-only tools may run without a question.</returns>
    public bool IsMcpServerTrusted(string serverName)
    {
        return TrustedMcpServers.Contains(serverName, StringComparer.Ordinal);
    }

    /// <summary>
    /// Get the protected folders as they apply now.
    /// </summary>
    /// <param name="currentSession">The id of the session that is running, whose own directory stays usable, or null.</param>
    /// <returns>The protected folders.</returns>
    public SensitivePaths Sensitive(string? currentSession = null)
    {
        return SensitivePaths.Default(_paths, currentSession, ProtectedFolders);
    }

    /// <summary>
    /// Get the settings a new chat starts with.
    /// </summary>
    /// <param name="hasFolder">True when the chat works in a folder the user opened, and false for a chat that only has its own files.</param>
    /// <returns>The settings. A chat without a folder starts <see cref="AccessLevel.ReadOnly"/>, because nobody chose a place for it to change.</returns>
    public PermissionSettings For(bool hasFolder)
    {
        var settings = Current;
        return hasFolder ? settings : settings with { Access = AccessLevel.ReadOnly };
    }

    /// <summary>
    /// Save the rules, replacing the old ones.
    /// </summary>
    /// <param name="rules">The rules in the order they apply. Rules with an empty pattern are dropped.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>A task that completes once the rules are saved.</returns>
    public Task SaveRulesAsync(IEnumerable<PermissionRule> rules, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var kept = rules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Pattern))
            .Select(rule => rule with { Pattern = rule.Pattern.Trim() })
            .ToList();
        return _config.UpdateAsync(config => config with { PermissionRules = kept }, cancellationToken);
    }

    /// <summary>
    /// Save the protected folders.
    /// </summary>
    /// <param name="folders">The folders to protect, or null to go back to the built-in list. Blank entries and duplicates are dropped.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>A task that completes once the folders are saved.</returns>
    public Task SaveProtectedFoldersAsync(IEnumerable<string>? folders, CancellationToken cancellationToken = default)
    {
        List<string>? kept = folders?
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(folder => folder.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return _config.UpdateAsync(config => config with { ProtectedFolders = kept }, cancellationToken);
    }

    /// <summary>
    /// Trust or stop trusting an MCP server.
    /// </summary>
    /// <param name="serverName">The server's name in <c>mcp.json</c>.</param>
    /// <param name="trusted">True to trust the server, and false to ask about all its tools again.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>A task that completes once the choice is saved.</returns>
    public Task SetMcpServerTrustedAsync(string serverName, bool trusted, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        return _config.UpdateAsync(
            config =>
            {
                var others = config.TrustedMcpServers.Where(name => !string.Equals(name, serverName, StringComparison.Ordinal));
                return config with { TrustedMcpServers = trusted ? [.. others, serverName] : [.. others] };
            },
            cancellationToken);
    }

    /// <summary>
    /// Save the global choices.
    /// </summary>
    /// <param name="defaults">What new chats start with. A risky option is saved as the built-in default instead.</param>
    /// <param name="allowBypass">Whether the risky options may be used at all.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>A task that completes once the choices are saved.</returns>
    public Task SaveAsync(PermissionSettings defaults, bool allowBypass, CancellationToken cancellationToken = default)
    {
        return _config.UpdateAsync(
            config => config with
            {
                DefaultAccess = defaults.Access is AccessLevel.ReadOnly ? AccessLevel.ReadOnly : PermissionSettings.Default.Access,
                DefaultApprovals = defaults.Approvals is ApprovalMode.AutoApprove ? ApprovalMode.AutoApprove : PermissionSettings.Default.Approvals,
                DisableBypassModes = !allowBypass,
            },
            cancellationToken);
    }
}
