using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Sandbox.Network;
using BlazeHarness.Core.Tools.Files;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// What a tool call runs against.
/// </summary>
public sealed class ToolContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolContext"/> class.
    /// </summary>
    /// <param name="folder">The working folder, which must exist. Links in it are followed so every later check compares real paths.</param>
    public ToolContext(string folder)
    {
        Root = RootedPath.CanonicalRoot(folder);
    }

    private ToolContext(string root, bool allowsOutside, ShellMode shell, IReadOnlyList<string> shellReadFolders, SensitivePaths sensitive, RefusedPaths refused, INetworkGate? network)
    {
        Root = root;
        AllowsOutside = allowsOutside;
        Shell = shell;
        ShellReadFolders = shellReadFolders;
        Sensitive = sensitive;
        Refused = refused;
        Network = network;
    }

    /// <summary>
    /// Gets the real path of the working folder, which file tools stay inside and relative paths resolve against.
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// Gets a value indicating whether paths outside the working folder may be used, because the call was approved or the access level allows it.
    /// </summary>
    public bool AllowsOutside { get; }

    /// <summary>
    /// Gets how far a shell command of this call may reach.
    /// </summary>
    public ShellMode Shell { get; } = ShellMode.Writable;

    /// <summary>
    /// Gets the folders outside the working folder that the user approved reading, which a shell command of this call may also read.
    /// </summary>
    public IReadOnlyList<string> ShellReadFolders { get; } = [];

    /// <summary>
    /// Gets the protected folders, which the listing and search tools skip while they walk the working folder.
    /// </summary>
    /// <remarks>
    /// The permission check only sees the paths a call names, so a walk that starts in a folder above a protected one must skip it itself.
    /// </remarks>
    public SensitivePaths Sensitive { get; } = SensitivePaths.None;

    /// <summary>
    /// Gets the paths the user's rules refuse, which the listing and search tools skip while they walk and a sandboxed command can't read.
    /// </summary>
    /// <remarks>
    /// Like <see cref="Sensitive"/>, a refuse rule is only checked against the path a call names, so a walk must apply it to every file it reaches.
    /// </remarks>
    public RefusedPaths Refused { get; } = RefusedPaths.None;

    /// <summary>
    /// Gets the gate that decides which hosts a sandboxed shell command may connect to, or null when the network is not filtered.
    /// </summary>
    public INetworkGate? Network { get; }

    private IReadOnlyList<string>? CheckedPaths { get; init; }

    /// <summary>
    /// Get a copy of this context that accepts paths outside the working folder.
    /// </summary>
    /// <remarks>
    /// Only for a call that has already passed the permission check.
    /// </remarks>
    /// <returns>A context with the same root that accepts any path.</returns>
    public ToolContext WithOutsideAllowed() => new(Root, allowsOutside: true, Shell, ShellReadFolders, Sensitive, Refused, Network);

    /// <summary>
    /// Get a copy of this context with a different shell mode.
    /// </summary>
    /// <param name="shell">How far a shell command may reach.</param>
    /// <returns>A context with the same root and path rules.</returns>
    public ToolContext WithShell(ShellMode shell) => new(Root, AllowsOutside, shell, ShellReadFolders, Sensitive, Refused, Network);

    /// <summary>
    /// Get a copy of this context with the folders a shell command may also read.
    /// </summary>
    /// <param name="folders">The approved folders outside the working folder.</param>
    /// <returns>A context with the same root and path rules.</returns>
    public ToolContext WithShellReadFolders(IReadOnlyList<string> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        return new(Root, AllowsOutside, Shell, folders, Sensitive, Refused, Network);
    }

    /// <summary>
    /// Get a copy of this context with the folders a walk must skip.
    /// </summary>
    /// <param name="sensitive">The protected folders.</param>
    /// <returns>A context with the same root and path rules.</returns>
    public ToolContext WithSensitive(SensitivePaths sensitive)
    {
        ArgumentNullException.ThrowIfNull(sensitive);
        return new(Root, AllowsOutside, Shell, ShellReadFolders, sensitive, Refused, Network);
    }

    /// <summary>
    /// Get a copy of this context that skips the paths a rule refuses while walking.
    /// </summary>
    /// <param name="refused">The paths the user's rules refuse.</param>
    /// <returns>A context with the same root and path rules.</returns>
    public ToolContext WithRefused(RefusedPaths refused)
    {
        ArgumentNullException.ThrowIfNull(refused);
        return new(Root, AllowsOutside, Shell, ShellReadFolders, Sensitive, refused, Network);
    }

    /// <summary>
    /// Get a copy of this context with the gate that filters a sandboxed shell command's network.
    /// </summary>
    /// <param name="network">The gate.</param>
    /// <returns>A context with the same root and path rules.</returns>
    public ToolContext WithNetwork(INetworkGate network)
    {
        ArgumentNullException.ThrowIfNull(network);
        return new(Root, AllowsOutside, Shell, ShellReadFolders, Sensitive, Refused, network);
    }

    /// <summary>
    /// Resolve a path from a call to a real path inside the working folder.
    /// </summary>
    /// <param name="input">The path as the model wrote it.</param>
    /// <param name="fullPath">The real path to use, when the result is true.</param>
    /// <param name="error">A message for the model, when the result is false.</param>
    /// <returns>True when the path is allowed.</returns>
    public bool TryResolve(string? input, out string fullPath, out string error)
    {
        if (!RootedPath.TryResolve(Root, input, out fullPath, out error, AllowsOutside))
        {
            return false;
        }

        if (Sensitive.IsProtected(fullPath) || Refused.IsRefused(fullPath))
        {
            error = "This path is protected or refused by a file path rule.";
            return false;
        }

        if (CheckedPaths is not null && !CheckedPaths.Contains(fullPath, Folders.FolderPaths.Comparer))
        {
            error = "The resolved path changed after the permission check. Request the call again.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Get a path relative to the working folder, written with forward slashes.
    /// </summary>
    /// <param name="fullPath">A path inside the working folder.</param>
    /// <returns>The relative path.</returns>
    public string Relative(string fullPath) => RootedPath.Relative(Root, fullPath);

    internal ToolContext WithCheckedPaths(IReadOnlyList<string>? paths) =>
        new(Root, AllowsOutside, Shell, ShellReadFolders, Sensitive, Refused, Network) { CheckedPaths = paths };
}
