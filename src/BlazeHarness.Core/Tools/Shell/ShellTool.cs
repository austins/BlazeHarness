using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Sandbox.Network;
using BlazeHarness.Core.Sandbox.Ports;
using BlazeHarness.Core.Tools.Files;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BlazeHarness.Core.Tools.Shell;

/// <summary>
/// Runs one command in the working folder.
/// </summary>
internal sealed class ShellTool : ITool
{
    private const int DefaultTimeoutSeconds = 120;
    private const int MaxTimeoutSeconds = 600;
    private const int DefaultBackgroundSeconds = 3600;
    private const int MaxBackgroundSeconds = 8 * 3600;
    private const string SandboxNetworkNote = " It runs in the sandbox, which has its own network: a port it opens can't be reached from other commands or the browser tools. Ask the user to set the access level to Unrestricted if they need to.";
    private const string ForwardedNote = " The ports it listens on are forwarded to localhost at the same number, for later commands and the browser tools; shell_jobs list shows them.";

    private static readonly TimeSpan OutputGrace = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The initial wait for a background command to exit or fail.
    /// </summary>
    /// <remarks>Catch immediate failures without holding up a command that starts a server.</remarks>
    private static readonly TimeSpan StartupWait = TimeSpan.FromSeconds(3);

    private readonly ShellInfo _shell;
    private readonly IShellSandbox _sandbox;
    private readonly string _scratchDirectory;
    private readonly BackgroundShells _background;
    private readonly TimeProvider _time;
    private readonly string? _gitDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellTool"/> class.
    /// </summary>
    /// <param name="shell">The shell to run commands in.</param>
    /// <param name="sandbox">Starts the commands, inside a sandbox when the call's shell mode asks for one.</param>
    /// <param name="scratchDirectory">A folder confined commands may always write to, for their home and temporary files.</param>
    /// <param name="background">Keeps the commands the model starts with <c>background</c>.</param>
    /// <param name="time">Times the command's time limit and the wait for its output.</param>
    /// <param name="gitDirectory">The repository's shared git folder when the working folder is a git worktree, or null.</param>
    public ShellTool(ShellInfo shell, IShellSandbox sandbox, string scratchDirectory, BackgroundShells background, TimeProvider time, string? gitDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(sandbox);
        ArgumentException.ThrowIfNullOrWhiteSpace(scratchDirectory);
        ArgumentNullException.ThrowIfNull(background);
        ArgumentNullException.ThrowIfNull(time);
        _shell = shell;
        _sandbox = sandbox;
        _scratchDirectory = scratchDirectory;
        _background = background;
        _time = time;
        _gitDirectory = gitDirectory;
    }

    /// <inheritdoc />
    public string Name => "shell";

    /// <inheritdoc />
    public string Description =>
        $"Run one command in {_shell.Name} with the working folder as the current directory, and return its combined output and exit code. " +
        "Use it for builds, tests, git and other programs. Use the file tools to read, search and edit files. " +
        "The command cannot read input, so avoid anything interactive. " +
        "A command that should keep running after the call, such as a dev server, a watcher or a database, must be started with background=true: " +
        "anything started with & or nohup is stopped when the call ends. A background call returns an id at once; read its output, wait for it or stop it with the shell_jobs tool, and stop it when you are done. " +
        Network +
        "Unless the user turned it off, the command runs in a sandbox that can only change the working folder and can't see the rest of the computer's files. " +
        "Each command starts fresh: /tmp is private to it and emptied when it ends, so keep files that later commands need under $TMPDIR, which lasts for the whole chat.";

    /// <inheritdoc />
    public string InputSchema => $$"""
        {
          "type": "object",
          "properties": {
            "command": { "type": "string", "description": "The command to run, written in {{_shell.Name}} syntax." },
            "timeout_seconds": { "type": "integer", "description": "How long to let it run before it is stopped. Defaults to {{DefaultTimeoutSeconds}}, at most {{MaxTimeoutSeconds}}. With background, it is how long the command may keep running: defaults to {{DefaultBackgroundSeconds}}, at most {{MaxBackgroundSeconds}}; set it to what the task needs." },
            "background": { "type": "boolean", "description": "Start the command and return at once while it keeps running, for servers and other commands that don't end. Defaults to false." }
          },
          "required": ["command"]
        }
        """;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public bool RunsCommands => true;

    /// <inheritdoc />
    public TimeSpan Timeout { get; } = TimeSpan.FromSeconds(MaxTimeoutSeconds + 30);

    private string Network => _background.Ports is null
        ? "A sandboxed command has a network of its own: a server it starts can't be reached from other commands or from the browser tools, only from inside the same command (for example start it with &, wait, curl it, then kill it). " +
          "To open a server in the browser or call it from later commands, the user must set the chat's access level to Unrestricted, which turns the sandbox off. "
        : "A sandboxed command has a network of its own, but the ports a background command listens on are forwarded at the same number to localhost, where later commands and the browser tools reach them. " +
          "A server started without background can only be reached from inside its own command. ";

    /// <inheritdoc />
    public string Summarize(JsonElement arguments)
    {
        var command = ToolSupport.Text(arguments, "command").Trim();
        var newline = command.IndexOf('\n', StringComparison.Ordinal);
        return newline < 0 ? command : command[..newline].TrimEnd() + " …";
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(context);

        var command = arguments.RequiredString("command");
        var background = arguments.OptionalBool("background");
        var seconds = background
            ? arguments.OptionalInt("timeout_seconds", 1, MaxBackgroundSeconds) ?? DefaultBackgroundSeconds
            : arguments.OptionalInt("timeout_seconds", 1, MaxTimeoutSeconds) ?? DefaultTimeoutSeconds;
        if (background && _background.RunningCount >= BackgroundShells.MaxRunning)
        {
            return ToolResult.Error(string.Create(CultureInfo.InvariantCulture, $"This command was not run: {BackgroundShells.MaxRunning} background commands are already running ({_background.Ids()}). Stop one with the shell_jobs tool first."));
        }

        IReadOnlyList<string>? refused = null;
        if (context.Shell != ShellMode.Unconfined && context.Refused.Any)
        {
            List<string> found = [];
            string? tooBroad = null;
            var complete = await Task.Run(() => RefusedFiles.TryFind(context, RefusedFiles.MaxEntries, RefusedFiles.MaxPaths, out found, out tooBroad, cancellationToken), cancellationToken);
            if (!complete)
            {
                return ToolResult.Error($"This command was not run: the user's rule refusing '{tooBroad}' covers too many files, or too many to look through, to keep them out of the command's reach. Use the file tools instead, or say what you wanted to do. The user can narrow or remove the rule in Settings > Permissions.");
            }

            refused = found;
        }

        var launch = new ShellLaunch(_shell, command, context.Root, context.Shell, _scratchDirectory, TimeSpan.FromSeconds(seconds), context.ShellReadFolders, refused, context.Shell == ShellMode.Unconfined ? null : context.Network, _gitDirectory);
        var ports = _background.Ports?.Prepare(launch, publishes: background);
        IShellProcess process;
        try
        {
            process = await _sandbox.StartAsync(ports?.Launch ?? launch, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            ports?.Dispose();
            return ToolResult.Error(ex.Message);
        }
        catch
        {
            ports?.Dispose();
            throw;
        }

        if (ports is not null)
        {
            await ports.AttachAsync(process, cancellationToken);
        }

        if (background)
        {
            return await RunInBackgroundAsync(process, command, TimeSpan.FromSeconds(seconds), context.Shell != ShellMode.Unconfined, ports, cancellationToken);
        }

        using (ports)
        using (process)
        {
            var output = new ShellOutput(context.OutputLine);
            var readers = Task.WhenAll(PumpAsync(process.Output, output), PumpAsync(process.Error, output));
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(seconds), _time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);
            int exitCode;
            try
            {
                exitCode = await process.WaitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill();
                await FinishReadingAsync(readers, _time);
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                return ToolResult.Error(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{output}\n[The command ran longer than {seconds} seconds and was stopped. If it is meant to keep running, such as a server, run it again with background=true.]"));
            }

            await FinishReadingAsync(readers, _time);
            var text = string.Create(CultureInfo.InvariantCulture, $"{output}{RefusalNote(launch.Network)}[exit code {exitCode}]");
            return exitCode == 0 ? ToolResult.Ok(text) : ToolResult.Error(text);
        }
    }

    internal static async Task FinishReadingAsync(Task readers, TimeProvider time)
    {
        try
        {
            await readers.WaitAsync(OutputGrace, time);
        }
        catch (TimeoutException)
        {
            // A program the command started is still holding the streams open; what it printed so far is enough.
        }
    }

    /// <summary>
    /// Describe forwarded ports for the model.
    /// </summary>
    /// <param name="ports">The ports.</param>
    /// <returns>The ports, separated by commas, each noting when it is only forwarded to other commands.</returns>
    internal static string DescribePorts(IReadOnlyList<ForwardedPort> ports)
    {
        return string.Join(", ", ports.Select(port => port.OnHost
            ? port.Port.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{port.Port} (only for other commands: another program uses it on this computer)")));
    }

    private static string RefusalNote(INetworkGate? gate)
    {
        return gate is { Refusals: { Count: > 0 } refusals }
            ? $"[Blocked by the network filter: {string.Join(' ', refusals)}]\n"
            : string.Empty;
    }

    private static string NetworkNote(bool sandboxed, BackgroundShell job, SandboxMember? ports)
    {
        if (!sandboxed)
        {
            return string.Empty;
        }

        if (ports is null)
        {
            return SandboxNetworkNote;
        }

        var forwarded = job.Ports;
        return forwarded.Count == 0 ? ForwardedNote : $"{ForwardedNote} So far: {DescribePorts(forwarded)}.";
    }

    private async Task<ToolResult> RunInBackgroundAsync(IShellProcess process, string command, TimeSpan limit, bool sandboxed, SandboxMember? ports, CancellationToken cancellationToken)
    {
        var output = new ShellOutput();
        var readers = Task.WhenAll(PumpAsync(process.Output, output), PumpAsync(process.Error, output));
        var job = _background.Start(process, command, limit, output, readers, ports);
        if (job is null)
        {
            process.Kill();
#pragma warning disable IDISP007 // The caller hands the process over, and nothing else will dispose it.
            process.Dispose();
            ports?.Dispose();
#pragma warning restore IDISP007
            return ToolResult.Error("This command was not run: the chat is closing.");
        }

        try
        {
            await job.Exit.WaitAsync(StartupWait, _time, cancellationToken);
        }
        catch (TimeoutException)
        {
            return ToolResult.Ok(string.Create(
                CultureInfo.InvariantCulture,
                $"{output}[Started in the background as {job.Id}; it is stopped after {limit.TotalSeconds:0} seconds, or when the chat is closed. Read its output, wait for it or stop it with the shell_jobs tool. To keep it longer, stop it and start it again with a larger timeout_seconds.{NetworkNote(sandboxed, job, ports)}]"));
        }
        catch (OperationCanceledException)
        {
            job.Stop();
            throw;
        }

        var text = string.Create(CultureInfo.InvariantCulture, $"{output}[{job.Id} ended right after it started: {job.Describe()}]");
        return job.ExitCode == 0 ? ToolResult.Ok(text) : ToolResult.Error(text);
    }

    private static async Task PumpAsync(Stream stream, ShellOutput output)
    {
        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var lines = new CappedLineReader(reader, ShellOutput.MaxLineCharacters);
            while (await lines.ReadLineAsync() is { } line)
            {
                output.AppendLine(lines.DroppedCharacters > 0
                    ? string.Create(CultureInfo.InvariantCulture, $"{line} [{lines.DroppedCharacters} more characters of this line left out]")
                    : line);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The command ended and closed its streams.
        }
    }
}
