namespace BlazeHarness.Core.Sandbox.Ports;

/// <summary>
/// One sandboxed command of a chat that takes part in port forwarding: it reaches the ports of the chat's background
/// commands, and when it is one itself, its own ports are forwarded.
/// </summary>
internal sealed class SandboxMember : IDisposable
{
    private readonly SandboxPorts _owner;

    internal SandboxMember(SandboxPorts owner, ShellLaunch launch, bool publishes, string? gate)
    {
        _owner = owner;
        Launch = launch;
        Publishes = publishes;
        Gate = gate;
    }

    /// <summary>
    /// Gets the command to start, which waits for the forwarded ports first when there are any.
    /// </summary>
    public ShellLaunch Launch { get; }

    /// <summary>
    /// Gets whether the command's own ports are forwarded.
    /// </summary>
    public bool Publishes { get; }

    /// <summary>
    /// Gets the ports of this command that are forwarded so far.
    /// </summary>
    public IReadOnlyList<ForwardedPort> Ports => _owner.PortsOf(this);

    /// <summary>
    /// Gets the file whose creation lets the command start, or null when it doesn't wait.
    /// </summary>
    internal string? Gate { get; }

    /// <summary>
    /// Gets or sets a process in the command's network, once it is found.
    /// </summary>
    internal int? NamespacePid { get; set; }

    /// <summary>
    /// Gets the relays that bring other commands' ports into this command's network, by port.
    /// </summary>
    internal Dictionary<int, List<NamespaceRelay>> Inbound { get; } = [];

    /// <summary>
    /// Gets the ports of this command whose relay could not start, so they aren't tried again.
    /// </summary>
    internal HashSet<int> Failed { get; } = [];

    /// <summary>
    /// Connect the started command.
    /// </summary>
    /// <param name="process">The started command.</param>
    /// <param name="cancellationToken">Stops the wait.</param>
    /// <returns>A task that completes once the command may start.</returns>
    public Task AttachAsync(IShellProcess process, CancellationToken cancellationToken) => _owner.AttachAsync(this, process, cancellationToken);

    /// <summary>
    /// Stop forwarding to and from the command.
    /// </summary>
    public void Dispose() => _owner.Leave(this);
}
