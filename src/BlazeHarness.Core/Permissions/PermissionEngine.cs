using BlazeHarness.Core.Folders;
using BlazeHarness.Core.Mcp.Tools;
using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Tools;
using BlazeHarness.Core.Tools.Files;
using BlazeHarness.Core.Tools.Web;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace BlazeHarness.Core.Permissions;

/// <summary>
/// Decides whether a tool call runs, needs the user's approval, or is refused, from the chat's settings and what the call touches.
/// </summary>
/// <remarks>
/// <para>
/// File paths are resolved first, with every link followed, so a link or <c>..</c> can't hide where a call really goes.
/// A path on the protected list is always refused, at every level. Reads outside the working folder ask unless the chat is
/// <see cref="AccessLevel.Unrestricted"/> or <see cref="ApprovalMode.NeverAsk"/>. Writes outside the folder need <see cref="AccessLevel.Unrestricted"/>.
/// </para>
/// <para>
/// Below <see cref="AccessLevel.Unrestricted"/> a shell command runs in the OS sandbox (<see cref="Sandbox"/>), which can only change the
/// working folder, or can't change anything at <see cref="AccessLevel.ReadOnly"/>. Without a sandbox it is refused, so a command never
/// runs unconfined unless the user chose <see cref="AccessLevel.Unrestricted"/>.
/// </para>
/// <para>
/// A command on the <see cref="Forbidden"/> list, such as a recursive delete, always asks, under every approval mode except
/// <see cref="ApprovalMode.NeverAsk"/>, which refuses it, and the answer can't be remembered.
/// <see cref="AccessLevel.Unrestricted"/> skips the list, because the user has already accepted that commands run unconfined.
/// <see cref="AccessLevel.ReadOnly"/> skips the rules whose only risk is changing files, because its sandbox already makes them fail.
/// </para>
/// <para>
/// An MCP tool is not covered by the access level, because the harness can't see what a server touches. It asks first, unless the chat is
/// <see cref="ApprovalMode.NeverAsk"/>, or the user trusts the server and the server marks the tool read-only and not destructive. A Read only
/// chat never skips the question, and refuses under <see cref="ApprovalMode.NeverAsk"/>.
/// </para>
/// <para>
/// <see cref="ApprovalMode.NeverAsk"/> never waits for the user: any call that would still ask, including one an ask rule or a safety rule
/// asks about, is refused instead, with a reason the model can read.
/// </para>
/// </remarks>
public sealed class PermissionEngine
{
    private const string FolderGrantPrefix = "dir:";
    private const string FileGrantPrefix = "file:";
    private const string NeverAsksRefusal = "This chat never asks, so it was refused. Do not retry it; say in your reply what you wanted to do and why.";

    private readonly Lock _gate = new();
    private readonly ConcurrentDictionary<string, byte> _grants = new(StringComparer.Ordinal);
    private PermissionSettings _settings = PermissionSettings.Default;

    /// <summary>
    /// Gets or sets whether <see cref="AccessLevel.Unrestricted"/> and <see cref="ApprovalMode.NeverAsk"/> may be in effect.
    /// </summary>
    /// <remarks>
    /// It is asked every time the settings are read. While it says no, a chat that has either option behaves as the next safer one
    /// (<see cref="AccessLevel.WriteInFolder"/> and <see cref="ApprovalMode.AutoApprove"/>), whatever was saved.
    /// </remarks>
    public Func<bool> BypassAllowed { get; set; } = static () => true;

    /// <summary>
    /// Gets or sets the access level and approval mode, which can change while the chat runs.
    /// </summary>
    public PermissionSettings Settings
    {
        get
        {
            PermissionSettings settings;
            lock (_gate)
            {
                settings = _settings;
            }

            // The callback is outside code, so it runs outside the lock.
            return BypassAllowed()
                ? settings
                : new PermissionSettings(
                    settings.Access == AccessLevel.Unrestricted ? AccessLevel.WriteInFolder : settings.Access,
                    settings.Approvals == ApprovalMode.NeverAsk ? ApprovalMode.AutoApprove : settings.Approvals);
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
    public Func<SensitivePaths> Sensitive { get; set; } = () => SensitivePaths.None;

    /// <summary>
    /// Gets or sets the folders outside the working folder that calls may always read without asking, such as the chat's attached files.
    /// </summary>
    /// <remarks>
    /// They are for reading only. Writes outside the working folder are decided as before, and the protected folders still win.
    /// </remarks>
    public Func<IReadOnlyList<string>> ReadableFolders { get; set; } = static () => [];

    /// <summary>
    /// Gets or sets the commands that need the user's approval every time.
    /// </summary>
    public ForbiddenCommands Forbidden { get; set; } = ForbiddenCommands.BuiltIn;

    /// <summary>
    /// Gets or sets where the user's allow, ask and deny rules come from, which is asked on every call so edits apply at once.
    /// </summary>
    public Func<IReadOnlyList<PermissionRule>> Rules { get; set; } = static () => [];

    /// <summary>
    /// Gets or sets whether the user trusts an MCP server, given its name.
    /// </summary>
    /// <remarks>
    /// Only a trusted server's tools can skip the question, and only when the server says they only read.
    /// </remarks>
    public Func<string, bool> TrustedMcpServer { get; set; } = static _ => false;

    /// <summary>
    /// Gets or sets whether a sandboxed shell command may only reach the hosts that <see cref="EvaluateNetworkHost"/> allows, which is asked on every call so a change applies at once.
    /// </summary>
    public Func<bool> NetworkFilter { get; set; } = static () => false;

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
        => Evaluate(tool, argumentsJson, context, out _);

    /// <summary>
    /// Get the paths the user's rules refuse, for the tools that walk folders and so see paths no call named.
    /// </summary>
    /// <param name="root">The working folder.</param>
    /// <returns>The refused paths, from the rules as they are now.</returns>
    public RefusedPaths Refused(string root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return new RefusedPaths(Rules(), root);
    }

    /// <summary>
    /// Remember an approval for the rest of the chat.
    /// </summary>
    /// <param name="grantKey">The key from <see cref="PermissionDecision.GrantKey"/>.</param>
    public void Remember(string grantKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(grantKey);
        _grants[grantKey] = 0;
    }

    /// <summary>
    /// Decide whether a sandboxed shell command may connect to a host.
    /// </summary>
    /// <remarks>
    /// A rule decides first: a deny rule refuses, an allow rule lets the command through, and an ask rule asks every time. Without a rule, a host the
    /// user allowed for the chat passes, and any other host asks, unless the chat is <see cref="ApprovalMode.NeverAsk"/>, which has nobody to ask and refuses.
    /// </remarks>
    /// <param name="host">The host name the command connects to.</param>
    /// <returns>The decision. For <see cref="PermissionVerdict.Ask"/> its <see cref="PermissionDecision.GrantKey"/> remembers an approval for the chat.</returns>
    public PermissionDecision EvaluateNetworkHost(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var rule = RuleMatcher.ForNetworkHost(Rules(), host);
        if (rule is { Action: PermissionVerdict.Deny })
        {
            return new PermissionDecision(PermissionVerdict.Deny, $"A rule you set refuses connecting to {host} ({rule.Pattern}).");
        }

        if (rule is { Action: PermissionVerdict.Allow })
        {
            return PermissionDecision.Allow;
        }

        var reason = $"The command wants to connect to {host}.";
        if (rule is not null)
        {
            return Settings.Approvals == ApprovalMode.NeverAsk
                ? new PermissionDecision(PermissionVerdict.Deny, $"A rule you set asks before connecting to {host} ({rule.Pattern}), and this chat never asks.")
                : new PermissionDecision(PermissionVerdict.Ask, $"A rule you set asks before connecting to {host} ({rule.Pattern}).");
        }

        var key = NetworkGrantKey(host);
        if (_grants.ContainsKey(key))
        {
            return PermissionDecision.Allow;
        }

        return Settings.Approvals == ApprovalMode.NeverAsk
            ? new PermissionDecision(PermissionVerdict.Deny, $"{host} is not on the list of hosts commands may connect to, and this chat never asks. The user can allow it with a host rule in Settings.")
            : new PermissionDecision(PermissionVerdict.Ask, reason, key);
    }

    internal PermissionDecision Evaluate(ITool tool, string argumentsJson, ToolContext context, out IReadOnlyList<string>? resolvedPaths)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(context);

        var settings = Settings;
        using var document = Parse(argumentsJson);
        var arguments = document.RootElement;

        // Resolving a path follows its links, so a path tool's paths are resolved once for both the access check and the rules.
        var paths = tool is IMcpTool or IWebTool or ISendToChatTool || tool.RunsCommands ? null : ResolvePaths(tool, arguments, context);
        resolvedPaths = paths;
        var decision = tool switch
        {
            IMcpTool mcp => EvaluateMcp(mcp, settings),
            IWebTool web => EvaluateWeb(web, arguments, settings),
            ISendToChatTool send => EvaluateSendToChat(send, arguments, settings),
            { RunsCommands: true } => EvaluateCommand(arguments, settings),
            _ => EvaluatePaths(tool, paths!, settings, context),
        };
        decision = ApplyRules(tool, arguments, paths, context, decision);
        if (decision is { Verdict: PermissionVerdict.Ask, GrantKey: { } key } && _grants.ContainsKey(key))
        {
            decision = PermissionDecision.Allow with { AllowOutside = decision.AllowOutside, Shell = decision.Shell };
        }

        if (decision.Verdict == PermissionVerdict.Ask && settings.Approvals == ApprovalMode.NeverAsk)
        {
            decision = new PermissionDecision(PermissionVerdict.Deny, $"{decision.Reason} {NeverAsksRefusal}");
        }

        return tool.RunsCommands && decision.Verdict != PermissionVerdict.Deny
            ? decision with { ShellReadFolders = GrantedFolders() }
            : decision;
    }

#pragma warning disable CA1308 // Host names are compared in lower case and the key is never shown.
    private static string NetworkGrantKey(string host) => "net:" + host.ToLowerInvariant();
#pragma warning restore CA1308

    private static PermissionDecision ApplyRule(PermissionRule rule, PermissionDecision decision)
    {
        if (rule.Action == PermissionVerdict.Deny)
        {
            return new PermissionDecision(PermissionVerdict.Deny, $"A rule you set refuses this ({rule.Pattern}).");
        }

        if (decision.Verdict == PermissionVerdict.Deny)
        {
            return decision;
        }

        if (rule.Action == PermissionVerdict.Ask)
        {
            return decision with
            {
                Verdict = PermissionVerdict.Ask,
                Reason = $"A rule you set asks before this ({rule.Pattern}).",
                GrantKey = null,
            };
        }

        return decision is { Verdict: PermissionVerdict.Ask, Sticky: false } ? PermissionDecision.Allow with { AllowOutside = decision.AllowOutside, Shell = decision.Shell } : decision;
    }

    private static JsonDocument Parse(string json)
    {
        try
        {
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        }
        catch (JsonException)
        {
            // Not a bypass: ToolArguments.Parse uses the same parser, so the tool refuses these arguments and never runs.
            return JsonDocument.Parse("{}");
        }
    }

    private static string? Text(JsonElement arguments, string name)
    {
        return arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private PermissionDecision ApplyRules(ITool tool, JsonElement arguments, List<string>? resolved, ToolContext context, PermissionDecision decision)
    {
        var rules = Rules();
        if (rules.Count == 0)
        {
            return decision;
        }

        PermissionRule? rule;
        if (tool is IMcpTool mcp)
        {
            rule = RuleMatcher.ForMcpTool(rules, mcp.ServerName, mcp.RemoteName);
        }
        else if (tool is IWebTool web)
        {
            rule = web.Describe(arguments) is { } request ? RuleMatcher.ForWeb(rules, request.Target) : null;
        }
        else if (tool is ISendToChatTool)
        {
            rule = null;
        }
        else if (tool.RunsCommands)
        {
            rule = RuleMatcher.ForCommand(rules, Text(arguments, "command") ?? string.Empty);
        }
        else
        {
            rule = RuleMatcher.ForPaths(rules, resolved ?? ResolvePaths(tool, arguments, context), context.Root);
        }

        return rule is null ? decision : ApplyRule(rule, decision);
    }

    private static PermissionDecision EvaluateWeb(IWebTool tool, JsonElement arguments, PermissionSettings settings)
    {
        // Malformed arguments are refused by the tool itself, so there is nothing to ask about.
        if (tool.Describe(arguments) is not { } request || settings.IsRisky)
        {
            return PermissionDecision.Allow;
        }

        return new PermissionDecision(PermissionVerdict.Ask, request.Reason, request.GrantKey);
    }

    private static PermissionDecision EvaluateSendToChat(ISendToChatTool tool, JsonElement arguments, PermissionSettings settings)
    {
        var resolution = tool.Plan(arguments);
        if (resolution.Plan is not { } plan)
        {
            return new PermissionDecision(PermissionVerdict.Deny, resolution.Error ?? "That chat can't be used.");
        }

        // Another folder's chat has its own files and access, and a Read only chat shouldn't start work anywhere, so these ask every time
        // (and are refused in a chat that never asks).
        if (!plan.SameScope || settings.Access == AccessLevel.ReadOnly)
        {
            return new PermissionDecision(PermissionVerdict.Ask, plan.Reason, Sticky: true);
        }

        return settings.Approvals == ApprovalMode.AskFirst
            ? new PermissionDecision(PermissionVerdict.Ask, plan.Reason, plan.Creates ? null : "chat:" + plan.ChatId)
            : PermissionDecision.Allow;
    }

    private PermissionDecision EvaluateMcp(IMcpTool tool, PermissionSettings settings)
    {
        if (settings.Access == AccessLevel.ReadOnly)
        {
            return settings.Approvals == ApprovalMode.NeverAsk
                ? new PermissionDecision(PermissionVerdict.Deny, "This chat is Read only, and an MCP tool can change things the harness can't see. Say what you wanted to do, and the user can change the access level.")
                : AskMcp(tool) with { Sticky = true, GrantKey = null };
        }

        if (settings.Approvals == ApprovalMode.NeverAsk)
        {
            return PermissionDecision.Allow;
        }

        return tool is { ReadOnlyHint: true, DestructiveHint: not true } && TrustedMcpServer(tool.ServerName)
            ? PermissionDecision.Allow
            : AskMcp(tool);
    }

    private static PermissionDecision AskMcp(IMcpTool tool)
    {
        return new PermissionDecision(
            PermissionVerdict.Ask,
            $"Calls the tool '{tool.RemoteName}' of the MCP server '{tool.ServerName}', which runs outside the sandbox and can reach whatever that server can.",
            "mcp:" + tool.ServerName + "/" + tool.RemoteName);
    }

    private PermissionDecision EvaluateCommand(JsonElement arguments, PermissionSettings settings)
    {
        var key = "shell:" + (Text(arguments, "command") ?? string.Empty).Trim();
        if (settings.Access == AccessLevel.Unrestricted)
        {
            return settings.Approvals == ApprovalMode.AskFirst
                ? new PermissionDecision(PermissionVerdict.Ask, Rules().Any(r => r is { Target: RuleTarget.FilePath, Action: PermissionVerdict.Deny, Pattern.Length: > 0 }) ? "Runs a command on your computer, outside the sandbox, so the files your rules refuse are in its reach." : "Runs a command on your computer.", key, Shell: ShellMode.Unconfined)
                : PermissionDecision.Allow with { Shell = ShellMode.Unconfined };
        }

        if (!Sandbox.IsAvailable)
        {
            return new PermissionDecision(
                PermissionVerdict.Deny,
                $"Commands can't run in this chat because there is no sandbox to confine them. {Sandbox.Detail} Say what you wanted to run, and the user can choose the Unrestricted access level.");
        }

        var mode = settings.Access == AccessLevel.ReadOnly ? ShellMode.ReadOnly : ShellMode.Writable;
        if (Forbidden.Find(Text(arguments, "command") ?? string.Empty, readOnly: mode == ShellMode.ReadOnly) is { } forbidden)
        {
            return new PermissionDecision(PermissionVerdict.Ask, $"{forbidden.Risk} This kind of command needs your approval every time.", GrantKey: null, Shell: mode, Sticky: true);
        }

        return settings.Approvals == ApprovalMode.AskFirst
            ? new PermissionDecision(
                PermissionVerdict.Ask,
                mode == ShellMode.ReadOnly ? "Runs a command in a sandbox that can't change files." : "Runs a command in a sandbox that can only change the working folder.",
                key,
                Shell: mode)
            : PermissionDecision.Allow with { Shell = mode };
    }

    private static List<string> ResolvePaths(ITool tool, JsonElement arguments, ToolContext context)
    {
        var paths = new List<string>();
        foreach (var name in tool.PathArguments)
        {
            if (RootedPath.TryLocate(context.Root, Text(arguments, name), out var full))
            {
                paths.Add(full);
            }
        }

        return paths;
    }

    private PermissionDecision EvaluatePaths(ITool tool, List<string> paths, PermissionSettings settings, ToolContext context)
    {
        if (paths.Find(Sensitive().IsProtected) is { } protectedPath)
        {
            return new PermissionDecision(PermissionVerdict.Deny, $"The path '{protectedPath}' holds credentials or settings that tools may not use.");
        }

        var outside = paths.Find(p => !RootedPath.IsInside(context.Root, p));
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

        if (settings.Access == AccessLevel.Unrestricted || settings.Approvals == ApprovalMode.NeverAsk || GrantCovers(outside) || IsReadable(outside))
        {
            return PermissionDecision.Allow with { AllowOutside = true };
        }

        return new PermissionDecision(
            PermissionVerdict.Ask,
            string.Create(CultureInfo.InvariantCulture, $"Reads '{outside}', which is outside the working folder."),
            (Directory.Exists(outside) ? FolderGrantPrefix : FileGrantPrefix) + outside,
            AllowOutside: true);
    }

    private static PermissionDecision EvaluateWrite(ITool tool, string? outside, PermissionSettings settings)
    {
        if (settings.Access == AccessLevel.ReadOnly)
        {
            return new PermissionDecision(PermissionVerdict.Deny, "This chat is Read only, so files can't be changed. Say what you wanted to change, and the user can change the access level.");
        }

        if (outside is not null)
        {
            if (settings.Access != AccessLevel.Unrestricted)
            {
                return new PermissionDecision(PermissionVerdict.Deny, $"The path '{outside}' is outside the working folder, and this chat only writes inside it. Say what you wanted to change.");
            }

            return settings.Approvals == ApprovalMode.AskFirst
                ? new PermissionDecision(PermissionVerdict.Ask, $"Changes '{outside}', which is outside the working folder.", GrantKey: null, AllowOutside: true)
                : PermissionDecision.Allow with { AllowOutside = true };
        }

        return settings.Approvals == ApprovalMode.AskFirst
            ? new PermissionDecision(PermissionVerdict.Ask, "Changes files in the working folder.", "tool:" + tool.Name)
            : PermissionDecision.Allow;
    }

    private List<string> GrantedFolders()
    {
        return [.. _grants.Keys.Where(IsFolderGrant).Select(key => key[FolderGrantPrefix.Length..]), .. ReadableFolders().Select(RootedPath.CanonicalRoot)];
    }

    private bool IsReadable(string fullPath)
    {
        return ReadableFolders().Any(folder => RootedPath.IsInside(RootedPath.CanonicalRoot(folder), fullPath));
    }

    private static bool IsFolderGrant(string key) => key.StartsWith(FolderGrantPrefix, StringComparison.Ordinal);

    private bool GrantCovers(string fullPath)
    {
        foreach (var key in _grants.Keys)
        {
            if (key.StartsWith(FolderGrantPrefix, StringComparison.Ordinal) && RootedPath.IsInside(key[FolderGrantPrefix.Length..], fullPath))
            {
                return true;
            }

            if (key.StartsWith(FileGrantPrefix, StringComparison.Ordinal) && FolderPaths.Comparer.Equals(key[FileGrantPrefix.Length..], fullPath))
            {
                return true;
            }
        }

        return false;
    }
}
