using System.Globalization;
using System.Text.Json;

namespace BlazeHarness.Core.Tools.Shell;

/// <summary>
/// Lists, reads and stops the commands the model started with <c>background</c>.
/// </summary>
/// <remarks>
/// It counts as read-only: it only reaches commands this chat started under the approval of the <c>shell</c> call, so stopping one needs no further approval.
/// </remarks>
internal sealed class ShellJobsTool : ITool
{
    private const int MaxWaitSeconds = 60;

    private static readonly TimeSpan StopWait = TimeSpan.FromSeconds(5);

    private readonly BackgroundShells _background;
    private readonly TimeProvider _time;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellJobsTool"/> class.
    /// </summary>
    /// <param name="background">The commands of the chat.</param>
    /// <param name="time">Times the waits.</param>
    public ShellJobsTool(BackgroundShells background, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(background);
        ArgumentNullException.ThrowIfNull(time);
        _background = background;
        _time = time;
    }

    /// <inheritdoc />
    public string Name => "shell_jobs";

    /// <inheritdoc />
    public string Description =>
        "Work with the commands you started with the shell tool's background option. " +
        "action=list shows them with their ids and status. " +
        "action=output returns what a command printed so far and whether it is still running; set wait_seconds to wait up to that long for it to end first, which is also how to give a starting server time. " +
        "action=stop stops one command and everything it started. Stop each command you no longer need. " +
        "The commands are stopped when the chat is closed or the app exits, so they are gone when a chat is resumed.";

    /// <inheritdoc />
    public string InputSchema => $$"""
        {
          "type": "object",
          "properties": {
            "action": { "type": "string", "enum": ["list", "output", "stop"], "description": "What to do." },
            "id": { "type": "string", "description": "The command's id, such as bg1. Required for output and stop." },
            "wait_seconds": { "type": "integer", "description": "For output: wait up to this many seconds for the command to end, at most {{MaxWaitSeconds}}. Defaults to 0." }
          },
          "required": ["action"]
        }
        """;

    /// <inheritdoc />
    public bool IsReadOnly => true;

    /// <inheritdoc />
    public TimeSpan Timeout { get; } = TimeSpan.FromSeconds(MaxWaitSeconds + 30);

    /// <inheritdoc />
    public string Summarize(JsonElement arguments)
    {
        var action = ToolSupport.Text(arguments, "action");
        var id = ToolSupport.Text(arguments, "id");
        return id.Length == 0 ? action : action + " " + id;
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(context);

        var action = arguments.RequiredString("action");
        if (action == "list")
        {
            return ToolResult.Ok(List());
        }

        if (action is not ("output" or "stop"))
        {
            return ToolResult.Error("The 'action' argument must be list, output or stop.");
        }

        var id = arguments.RequiredString("id");
        if (_background.Find(id) is not { } job)
        {
            return ToolResult.Error($"There is no background command '{id}'. Running and finished commands of this chat: {_background.Ids()}. Commands don't survive closing the chat.");
        }

        return action == "stop"
            ? await StopAsync(job, cancellationToken)
            : await OutputAsync(job, arguments.OptionalInt("wait_seconds", 0, MaxWaitSeconds) ?? 0, cancellationToken);
    }

    private string List()
    {
        var jobs = _background.Jobs;
        if (jobs.Count == 0)
        {
            return "No background commands.";
        }

        var now = _time.GetUtcNow();
        return string.Join('\n', jobs.Select(job => string.Create(
            CultureInfo.InvariantCulture,
            $"{job.Id}  {Status(job)}  started {(now - job.StartedAt).TotalSeconds:0}s ago{Ports(job)}  {FirstLine(job.Command)}")));
    }

    private async Task<ToolResult> OutputAsync(BackgroundShell job, int wait, CancellationToken cancellationToken)
    {
        if (wait > 0 && job.IsRunning)
        {
            try
            {
                await job.Exit.WaitAsync(TimeSpan.FromSeconds(wait), _time, cancellationToken);
            }
            catch (TimeoutException)
            {
                // Still running; report what it has printed.
            }
        }

        return ToolResult.Ok(Report(job));
    }

    private async Task<ToolResult> StopAsync(BackgroundShell job, CancellationToken cancellationToken)
    {
        if (!job.Stop())
        {
            return ToolResult.Ok($"{job.Id} had already ended ({job.Describe()}).");
        }

        try
        {
            await job.Exit.WaitAsync(StopWait, _time, cancellationToken);
        }
        catch (TimeoutException)
        {
            return ToolResult.Error($"{job.Id} was told to stop but is still ending. Check it again with action=list.");
        }

        return ToolResult.Ok($"Stopped {job.Id}.\n{Report(job)}");
    }

    private static string Report(BackgroundShell job) => $"{job.Output}[{job.Id}: {job.Describe()}]";

    private static string Status(BackgroundShellInfo job)
    {
        return job.Status switch
        {
            BackgroundShellStatus.Running => "running",
            BackgroundShellStatus.Stopped => "stopped",
            BackgroundShellStatus.TimedOut => "stopped at its time limit",
            _ => string.Create(CultureInfo.InvariantCulture, $"exited with code {job.ExitCode}"),
        };
    }

    private string Ports(BackgroundShellInfo info)
    {
        var ports = info.Status == BackgroundShellStatus.Running && _background.Find(info.Id) is { } job ? job.Ports : [];
        return ports.Count == 0 ? string.Empty : "  ports " + ShellTool.DescribePorts(ports);
    }

    private static string FirstLine(string command)
    {
        var text = command.Trim();
        var newline = text.IndexOf('\n', StringComparison.Ordinal);
        return newline < 0 ? text : text[..newline].TrimEnd() + " …";
    }
}
