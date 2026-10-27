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

    /// <summary>
    /// Initializes a new instance of the <see cref="PermissionDefaults"/> class.
    /// </summary>
    /// <param name="config">The store of the app configuration file.</param>
    public PermissionDefaults(JsonFileStore<AppConfig> config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
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
