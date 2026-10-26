using System.ComponentModel;
using System.Diagnostics;
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

    private readonly ShellInfo _shell;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellTool"/> class.
    /// </summary>
    /// <param name="shell">The shell to run commands in.</param>
    public ShellTool(ShellInfo shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
    }

    /// <inheritdoc />
    public string Name => "shell";

    /// <inheritdoc />
    public string Description =>
        $"Run one command in {_shell.Name} with the working folder as the current directory, and return its combined output and exit code. " +
        "Use it for builds, tests, git and other programs. Use the file tools to read, search and edit files. " +
        "The command cannot read input, so avoid anything interactive.";

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

        using var process = new Process { StartInfo = CreateStartInfo(command, context.Root) };
        var output = new ShellOutput();
        process.OutputDataReceived += (_, e) => Append(output, e.Data);
        process.ErrorDataReceived += (_, e) => Append(output, e.Data);

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return ToolResult.Error($"Could not start {_shell.Name}: {ex.Message}");
        }

        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return ToolResult.Error(string.Create(
                CultureInfo.InvariantCulture,
                $"{output}\n[The command ran longer than {seconds} seconds and was stopped.]"));
        }

        var exitCode = process.ExitCode;
        var text = string.Create(CultureInfo.InvariantCulture, $"{output}[exit code {exitCode}]");
        return exitCode == 0 ? ToolResult.Ok(text) : ToolResult.Error(text);
    }

    private static void Append(ShellOutput output, string? line)
    {
        if (line is not null)
        {
            output.AppendLine(line);
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // The process had already exited.
        }
    }

    private ProcessStartInfo CreateStartInfo(string command, string workingDirectory)
    {
        var info = new ProcessStartInfo(_shell.Executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in _shell.ArgumentsFor(command))
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }
}
