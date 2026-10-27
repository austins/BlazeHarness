using BlazeHarness.Core.Sandbox;
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
/// Below <see cref="AccessLevel.Unrestricted"/> a shell command runs in the OS sandbox (<see cref="Sandbox"/>), which can only change the
/// working folder, or can't change anything at <see cref="AccessLevel.ReadOnly"/>. Without a sandbox it is refused, so a command never
/// runs unconfined unless the user chose <see cref="AccessLevel.Unrestricted"/>.
/// </para>
/// <para>
/// A command on the <see cref="Forbidden"/> list, such as a recursive delete, always asks, under every approval mode, and the answer can't be
/// remembered. <see cref="AccessLevel.Unrestricted"/> skips the list, because the user has already accepted that commands run unconfined.
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
    /// Gets or sets the commands that need the user's approval every time.
    /// </summary>
    public ForbiddenCommands Forbidden { get; set; } = ForbiddenCommands.BuiltIn;

    /// <summary>
    /// Gets or sets whether shell commands can run inside an OS sandbox that enforces the access level.
    /// </summary>
    public SandboxStatus Sandbox { get; set; } = SandboxStatus.Unavailable("No sandbox is set up.");

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
            ? PermissionDecision.Allow with { AllowOutside = decision.AllowOutside, Shell = decision.Shell }
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
        var key = "shell:" + (Text(arguments, "command") ?? string.Empty).Trim();
        if (settings.Access == AccessLevel.Unrestricted)
        {
            return settings.Approvals == ApprovalMode.AskFirst
                ? new PermissionDecision(PermissionVerdict.Ask, "Runs a command on your computer.", key, Shell: ShellMode.Unconfined)
                : PermissionDecision.Allow with { Shell = ShellMode.Unconfined };
        }

        if (!Sandbox.IsAvailable)
        {
            return new PermissionDecision(
                PermissionVerdict.Deny,
                $"Commands can't run in this session because there is no sandbox to confine them. {Sandbox.Detail} Say what you wanted to run, and the user can choose the Unrestricted access level.");
        }

        var mode = settings.Access == AccessLevel.ReadOnly ? ShellMode.ReadOnly : ShellMode.Writable;
        if (Forbidden.Find(Text(arguments, "command") ?? string.Empty) is { } forbidden)
        {
            return new PermissionDecision(PermissionVerdict.Ask, $"{forbidden.Risk} This kind of command needs your approval every time.", GrantKey: null, Shell: mode);
        }

        return settings.Approvals == ApprovalMode.AskFirst
            ? new PermissionDecision(
                PermissionVerdict.Ask,
                mode == ShellMode.ReadOnly ? "Runs a command in a sandbox that can't change files." : "Runs a command in a sandbox that can only change the working folder.",
                key,
                Shell: mode)
            : PermissionDecision.Allow with { Shell = mode };
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
