using BlazeHarness.Core.Sandbox;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BlazeHarness.Core.Tools;

/// <summary>
/// Runs one command in the working folder.
/// </summary>
public sealed class ShellTool : ITool
{
    private const int DefaultTimeoutSeconds = 120;
    private const int MaxTimeoutSeconds = 600;

    private static readonly TimeSpan OutputGrace = TimeSpan.FromSeconds(2);

    private readonly ShellInfo _shell;
    private readonly IShellSandbox _sandbox;
    private readonly string _scratchDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellTool"/> class.
    /// </summary>
    /// <param name="shell">The shell to run commands in.</param>
    /// <param name="sandbox">Starts the commands, inside a sandbox when the call's shell mode asks for one.</param>
    /// <param name="scratchDirectory">A folder confined commands may always write to, for their home and temporary files.</param>
    public ShellTool(ShellInfo shell, IShellSandbox sandbox, string scratchDirectory)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(sandbox);
        ArgumentException.ThrowIfNullOrWhiteSpace(scratchDirectory);
        _shell = shell;
        _sandbox = sandbox;
        _scratchDirectory = scratchDirectory;
    }

    /// <inheritdoc />
    public string Name => "shell";

    /// <inheritdoc />
    public string Description =>
        $"Run one command in {_shell.Name} with the working folder as the current directory, and return its combined output and exit code. " +
        "Use it for builds, tests, git and other programs. Use the file tools to read, search and edit files. " +
        "The command cannot read input, so avoid anything interactive. " +
        "Unless the user turned it off, the command runs in a sandbox that can only change the working folder and can't see the rest of the computer's files.";

    /// <inheritdoc />
    public string InputSchema => $$"""
        {
          "type": "object",
          "properties": {
            "command": { "type": "string", "description": "The command to run, written in {{_shell.Name}} syntax." },
            "timeout_seconds": { "type": "integer", "description": "How long to let it run before it is stopped. Defaults to {{DefaultTimeoutSeconds}}, at most {{MaxTimeoutSeconds}}." }
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
        var seconds = arguments.OptionalInt("timeout_seconds", 1, MaxTimeoutSeconds) ?? DefaultTimeoutSeconds;

        var launch = new ShellLaunch(_shell, command, context.Root, context.Shell, _scratchDirectory, TimeSpan.FromSeconds(seconds), context.ShellReadFolders);
        IShellProcess process;
        try
        {
            process = await _sandbox.StartAsync(launch, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return ToolResult.Error(ex.Message);
        }

        using (process)
        {
            var output = new ShellOutput();
            var readers = Task.WhenAll(PumpAsync(process.Output, output), PumpAsync(process.Error, output));
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);
            int exitCode;
            try
            {
                exitCode = await process.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                process.Kill();
                await FinishReadingAsync(readers).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                return ToolResult.Error(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{output}\n[The command ran longer than {seconds} seconds and was stopped.]"));
            }

            await FinishReadingAsync(readers).ConfigureAwait(false);
            var text = string.Create(CultureInfo.InvariantCulture, $"{output}[exit code {exitCode}]");
            return exitCode == 0 ? ToolResult.Ok(text) : ToolResult.Error(text);
        }
    }

    private static async Task PumpAsync(Stream stream, ShellOutput output)
    {
        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var lines = new CappedLineReader(reader, ShellOutput.MaxLineCharacters);
            while (await lines.ReadLineAsync().ConfigureAwait(false) is { } line)
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

    private static async Task FinishReadingAsync(Task readers)
    {
        try
        {
            await readers.WaitAsync(OutputGrace).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // A program the command started is still holding the streams open; what it printed so far is enough.
        }
    }
}
