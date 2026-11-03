namespace BlazeHarness.Core.Tools.Shell;

/// <summary>
/// A snapshot of one background command.
/// </summary>
/// <param name="Id">The id the model and the UI use to refer to the command.</param>
/// <param name="Command">The command text.</param>
/// <param name="Status">How the command stands.</param>
/// <param name="ExitCode">The exit code, or null while the command is running.</param>
/// <param name="StartedAt">When the command was started.</param>
/// <param name="Limit">How long the command may run before it is stopped.</param>
public sealed record BackgroundShellInfo(
    string Id,
    string Command,
    BackgroundShellStatus Status,
    int? ExitCode,
    DateTimeOffset StartedAt,
    TimeSpan Limit);
