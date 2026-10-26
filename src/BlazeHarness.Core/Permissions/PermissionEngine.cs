using BlazeHarness.Core.Tools;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace BlazeHarness.Core.Permissions;

/// <summary>
/// Decides whether a tool call runs, needs the user's approval, or is refused, from the session's settings and what the call touches.
/// </summary>
/// <remarks>
/// <para>
/// File paths are resolved first, with every link followed, so a link or <c>..</c> can't hide where a call really goes.
/// A path on the protected list is always refused, at every level. Reads outside the working folder ask unless the session is
/// <see cref="AccessLevel.Unrestricted"/> or <see cref="ApprovalMode.NeverAsk"/>. Writes outside the folder need <see cref="AccessLevel.Unrestricted"/>.
/// </para>
/// <para>
/// Until the shell runs inside an OS sandbox (<see cref="ShellConfined"/>), a command can do anything the user can,
/// so below <see cref="AccessLevel.Unrestricted"/> it always asks, and at <see cref="AccessLevel.ReadOnly"/> it is refused.
/// </para>
/// </remarks>
public sealed class PermissionEngine
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, byte> _grants = new(StringComparer.Ordinal);
    private PermissionSettings _settings = PermissionSettings.Default;

    /// <summary>
    /// Gets or sets the access level and approval mode, which can change while the session runs.
    /// </summary>
    public PermissionSettings Settings
    {
        get
        {
            lock (_gate)
            {
                return _settings;
            }
        }

        set
        {
            lock (_gate)
            {
                _settings = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets the folders no call may touch.
    /// </summary>
    public SensitivePaths Sensitive { get; set; } = SensitivePaths.None;

    /// <summary>
    /// Gets or sets a value indicating whether shell commands run inside an OS sandbox that enforces the access level.
    /// </summary>
    public bool ShellConfined { get; set; }

    /// <summary>
    /// Decide what to do with a tool call.
    /// </summary>
    /// <param name="tool">The tool the model called.</param>
    /// <param name="argumentsJson">The call arguments as a JSON object string.</param>
    /// <param name="context">The working folder the call runs against.</param>
    /// <returns>The decision.</returns>
    public PermissionDecision Evaluate(ITool tool, string argumentsJson, ToolContext context)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(context);

        var settings = Settings;
        using var document = Parse(argumentsJson);
        var arguments = document.RootElement;

        var decision = tool.RunsCommands
            ? EvaluateCommand(arguments, settings)
            : EvaluatePaths(tool, arguments, settings, context);
        return decision is { Verdict: PermissionVerdict.Ask, GrantKey: { } key } && _grants.ContainsKey(key)
            ? PermissionDecision.Allow with { AllowOutside = decision.AllowOutside }
            : decision;
    }

    /// <summary>
    /// Remember an approval for the rest of the session.
    /// </summary>
    /// <param name="grantKey">The key from <see cref="PermissionDecision.GrantKey"/>.</param>
    public void Remember(string grantKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(grantKey);
        _grants[grantKey] = 0;
    }

    private static JsonDocument Parse(string json)
    {
        try
        {
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}");
        }
    }

    private static string? Text(JsonElement arguments, string name)
    {
        return arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string FolderOf(string fullPath) => Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath) ?? fullPath;

    private PermissionDecision EvaluateCommand(JsonElement arguments, PermissionSettings settings)
    {
        if (settings.Access == AccessLevel.ReadOnly)
        {
            return new PermissionDecision(PermissionVerdict.Deny, "This session is Read only, so commands can't run. Say what you wanted to run, and the user can change the access level.");
        }

        var key = "shell:" + (Text(arguments, "command") ?? string.Empty).Trim();
        if (settings.Access == AccessLevel.Unrestricted)
        {
            return settings.Approvals == ApprovalMode.AskFirst
                ? new PermissionDecision(PermissionVerdict.Ask, "Runs a command on your computer.", key)
                : PermissionDecision.Allow;
        }

        if (!ShellConfined)
        {
            return new PermissionDecision(PermissionVerdict.Ask, "Runs a command on your computer. Commands are not confined to the working folder yet.", key);
        }

        return settings.Approvals == ApprovalMode.AskFirst
            ? new PermissionDecision(PermissionVerdict.Ask, "Runs a command on your computer.", key)
            : PermissionDecision.Allow;
    }

    private PermissionDecision EvaluatePaths(ITool tool, JsonElement arguments, PermissionSettings settings, ToolContext context)
    {
        var paths = new List<string>();
        foreach (var name in tool.PathArguments)
        {
            if (WorkspacePath.TryLocate(context.Root, Text(arguments, name), out var full))
            {
                paths.Add(full);
            }
        }

        if (paths.Find(Sensitive.IsProtected) is { } protectedPath)
        {
            return new PermissionDecision(PermissionVerdict.Deny, $"The path '{protectedPath}' holds credentials or settings that tools may not use.");
        }

        var outside = paths.Find(p => !WorkspacePath.IsInside(context.Root, p));
        return tool.IsReadOnly
            ? EvaluateRead(outside, settings)
            : EvaluateWrite(tool, outside, settings);
    }

    private PermissionDecision EvaluateRead(string? outside, PermissionSettings settings)
    {
        if (outside is null)
        {
            return PermissionDecision.Allow;
        }

        if (settings.Access == AccessLevel.Unrestricted || settings.Approvals == ApprovalMode.NeverAsk || GrantCovers(outside))
        {
            return PermissionDecision.Allow with { AllowOutside = true };
        }

        return new PermissionDecision(
            PermissionVerdict.Ask,
            string.Create(CultureInfo.InvariantCulture, $"Reads '{outside}', which is outside the working folder."),
            "dir:" + FolderOf(outside),
            AllowOutside: true);
    }

    private static PermissionDecision EvaluateWrite(ITool tool, string? outside, PermissionSettings settings)
    {
        if (settings.Access == AccessLevel.ReadOnly)
        {
            return new PermissionDecision(PermissionVerdict.Deny, "This session is Read only, so files can't be changed. Say what you wanted to change, and the user can change the access level.");
        }

        if (outside is not null)
        {
            if (settings.Access != AccessLevel.Unrestricted)
            {
                return new PermissionDecision(PermissionVerdict.Deny, $"The path '{outside}' is outside the working folder, and this session only writes inside it. Say what you wanted to change.");
            }

            return settings.Approvals == ApprovalMode.AskFirst
                ? new PermissionDecision(PermissionVerdict.Ask, $"Changes '{outside}', which is outside the working folder.", GrantKey: null, AllowOutside: true)
                : PermissionDecision.Allow with { AllowOutside = true };
        }

        return settings.Approvals == ApprovalMode.AskFirst
            ? new PermissionDecision(PermissionVerdict.Ask, "Changes files in the working folder.", "tool:" + tool.Name)
            : PermissionDecision.Allow;
    }

    private bool GrantCovers(string fullPath)
    {
        return _grants.Keys.Any(key => key.StartsWith("dir:", StringComparison.Ordinal) && WorkspacePath.IsInside(key["dir:".Length..], fullPath));
    }
}
